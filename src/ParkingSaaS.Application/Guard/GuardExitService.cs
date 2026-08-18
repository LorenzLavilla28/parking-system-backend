using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Audit;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Application.Pricing;
using ParkingSaaS.Contracts.Guard;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Domain.Payments;

namespace ParkingSaaS.Application.Guard;

/// <summary>
/// Exit validation. The backend is the source of truth: the status banner and the
/// approval both recompute the fee and outstanding balance server-side, never
/// trusting the guard's screen. Exit is allowed only when nothing is outstanding.
/// A supervisor override may release an unpaid or overdue session during an
/// approved operational exception.
/// </summary>
public sealed class GuardExitService : IGuardExitService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ISessionPricingService _pricing;
    private readonly IAuditLogger _audit;
    private readonly IDateTime _clock;
    private readonly ISessionRealtimeNotifier _realtime;
    private readonly IPaymentCheckoutCleanupService _checkoutCleanup;
    private readonly IParkingTokenService _tokens;
    private readonly ILogger<GuardExitService> _logger;

    public GuardExitService(
        IApplicationDbContext db, ICurrentUser user, ISessionPricingService pricing,
        IAuditLogger audit, IDateTime clock, ISessionRealtimeNotifier realtime,
        IPaymentCheckoutCleanupService checkoutCleanup, IParkingTokenService tokens,
        ILogger<GuardExitService> logger)
    {
        _db = db;
        _user = user;
        _pricing = pricing;
        _audit = audit;
        _clock = clock;
        _realtime = realtime;
        _checkoutCleanup = checkoutCleanup;
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<ExitStatusResponse> GetExitStatusAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await LoadAsync(sessionId, track: false, ct);
        await GuardLocationAccess.EnsureCanOperateAsync(_db, _user, session.ParkingLocationId, ct);

        var now = _clock.UtcNow;
        var (result, calculated) = await CalculateAsync(session, now, ct);
        var outstanding = session.Outstanding(calculated);
        var effectiveFee = session.EffectiveFee(calculated);

        var effectiveStatus = session.EffectiveStatus(now, calculated);
        var (decision, canExit) = Decide(effectiveStatus, outstanding, session.TotalPaid);

        return new ExitStatusResponse(
            session.Id, session.PlateNumberRaw, session.VehicleType.ToString(), session.Notes, effectiveStatus.ToString(),
            decision, result is not null, result?.Currency ?? "PHP",
            effectiveFee, session.TotalPaid, outstanding, session.EntryTime, session.PaidExitDeadline, canExit);
    }

    public async Task<ExitApprovedResponse> ApproveExitAsync(ApproveExitRequest request, string? ipAddress, CancellationToken ct)
    {
        var session = await LoadAsync(request.SessionId, track: true, ct);
        await GuardLocationAccess.EnsureCanOperateAsync(_db, _user, session.ParkingLocationId, ct);

        var now = _clock.UtcNow;
        var (result, calculated) = await CalculateAsync(session, now, ct);
        session.RefreshTimeBasedStatus(now, calculated);
        if (!session.Status.IsActive())
            throw new ConflictException("This session is already closed.");

        var outstanding = session.Outstanding(calculated);
        var finalFee = session.EffectiveFee(calculated);

        var hasOverride = !string.IsNullOrWhiteSpace(request.OverrideReason);
        var cashPaymentAmount = request.CashPaymentAmount ?? 0m;
        if (cashPaymentAmount < 0m)
            throw new ConflictException("The cash amount cannot be negative.");
        if (cashPaymentAmount > 0m && !hasOverride)
            throw new ConflictException("A cash amount can only be recorded with a supervisor override.");
        if (cashPaymentAmount > outstanding)
            throw new ConflictException("The cash amount cannot exceed the outstanding balance.");
        if (outstanding > 0m)
        {
            // Forcing exit past an outstanding balance requires a supervisor + reason.
            if (!hasOverride)
                throw new ConflictException("An outstanding balance must be paid before exit.");
            if (!IsSupervisorOrAdmin())
                throw new ForbiddenException("Only a supervisor can approve exit with an outstanding balance.");
        }

        // A session that is being closed must not retain a payable checkout.
        // Expire it before the session mutation so a late provider confirmation
        // cannot race with the exit decision.
        await _checkoutCleanup.CloseOpenCheckoutsAsync(session.Id, ct);

        var before = new { session.Status, session.TotalPaid, Outstanding = outstanding };

        Payment? cashPayment = null;
        if (cashPaymentAmount > 0m)
        {
            var receiptNumber = $"CR-{now:yyyyMMdd}-{_tokens.GenerateTicketCode()}";
            var reference = _tokens.GeneratePublicToken();
            cashPayment = Payment.CreateCashPaid(
                session.TenantId, session.Id, feeQuoteId: null, result?.Currency ?? "PHP", cashPaymentAmount,
                _tokens.Hash(reference), _tokens.Protect(reference), now, receiptNumber, _user.UserId ?? Guid.Empty);
            var deadline = await _pricing.GetPaidExitDeadlineAsync(session, now, ct);
            session.RegisterPayment(cashPaymentAmount, deadline);
            await _db.Payments.AddAsync(cashPayment, ct);
            await _audit.AddAsync(
                session.TenantId, session.ParkingLocationId, "CashPaymentRecorded",
                nameof(Payment), cashPayment.Id.ToString(), oldValues: null,
                new { amount = cashPaymentAmount, receiptNumber, session.TotalPaid },
                reason: request.OverrideReason, new AuditContext(ipAddress, request.DeviceInformation), ct);
        }

        session.ApproveExit(_user.UserId ?? Guid.Empty, now, finalFee, request.ExitPhotoUrl);

        await _audit.AddAsync(
            session.TenantId, session.ParkingLocationId,
            hasOverride ? "ExitApprovedWithOverride" : "ExitApproved",
            nameof(ParkingSession), session.Id.ToString(),
            before, new { Status = session.Status.ToString(), FinalFee = finalFee, session.TotalPaid },
            request.OverrideReason, new AuditContext(ipAddress, request.DeviceInformation), ct);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Exit approved for session {SessionId} (final fee {Fee})", session.Id, finalFee);

        await _realtime.SessionChangedAsync(
            session.TenantId, session.ParkingLocationId,
            new SessionRealtimeEvent(session.Id, session.ParkingLocationId, session.Status.ToString(),
                session.PlateNumberRaw, SessionEventKind.Exited), ct);

        return new ExitApprovedResponse(session.Id, session.Status.ToString(), finalFee, session.TotalPaid, now);
    }

    private static (string Decision, bool CanExit) Decide(ParkingSessionStatus status, decimal outstanding, decimal totalPaid)
    {
        if (status is ParkingSessionStatus.Exited or ParkingSessionStatus.Void or ParkingSessionStatus.Cancelled)
            return ("Closed", false);
        if (status == ParkingSessionStatus.OverstayDue)
            return ("AdditionalPaymentRequired", false);
        if (outstanding <= 0m)
            return (totalPaid > 0m ? "Paid" : "Free", true);
        return (totalPaid > 0m ? "AdditionalPaymentRequired" : "NotPaid", false);
    }

    private async Task<(Domain.Pricing.FeeCalculationResult? Result, decimal Calculated)> CalculateAsync(ParkingSession session, DateTimeOffset now, CancellationToken ct)
    {
        var result = await _pricing.CalculateAsync(session, now, discount: null, ct);
        return (result, result?.TotalAmount ?? 0m);
    }

    private async Task<ParkingSession> LoadAsync(Guid id, bool track, CancellationToken ct)
    {
        var query = _db.ParkingSessions.AsQueryable();
        if (!track) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Parking session not found.");
    }

    private bool IsSupervisorOrAdmin()
        => _user.Roles.Contains(RoleType.Supervisor) || _user.Roles.Contains(RoleType.TenantAdministrator);
}

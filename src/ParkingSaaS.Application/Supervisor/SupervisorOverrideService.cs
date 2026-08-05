using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Audit;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Contracts.Supervisor;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Services;

namespace ParkingSaaS.Application.Supervisor;

/// <summary>
/// Implements supervisor overrides. Each method snapshots the session before the
/// change, applies the domain transition, and records an audit entry with the
/// required reason and before/after values in the same transaction.
/// </summary>
public sealed class SupervisorOverrideService : ISupervisorOverrideService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPlateNormalizer _plateNormalizer;
    private readonly IAuditLogger _audit;
    private readonly IDateTime _clock;
    private readonly ISessionRealtimeNotifier _realtime;
    private readonly IPaymentCheckoutCleanupService _checkoutCleanup;
    private readonly ILogger<SupervisorOverrideService> _logger;

    public SupervisorOverrideService(
        IApplicationDbContext db, ICurrentUser user, IPlateNormalizer plateNormalizer,
        IAuditLogger audit, IDateTime clock, ISessionRealtimeNotifier realtime,
        IPaymentCheckoutCleanupService checkoutCleanup, ILogger<SupervisorOverrideService> logger)
    {
        _db = db;
        _user = user;
        _plateNormalizer = plateNormalizer;
        _audit = audit;
        _clock = clock;
        _realtime = realtime;
        _checkoutCleanup = checkoutCleanup;
        _logger = logger;
    }

    public async Task<OverrideResponse> VoidAsync(VoidSessionRequest r, string? ip, CancellationToken ct)
        => await ApplyAsync(r.SessionId, r.Reason, ip, r.DeviceInformation, "SessionVoided",
            s => s.Void(), ct);

    public async Task<OverrideResponse> MarkComplimentaryAsync(ComplimentaryRequest r, string? ip, CancellationToken ct)
        => await ApplyAsync(r.SessionId, r.Reason, ip, r.DeviceInformation, "MarkedComplimentary",
            s => s.MarkComplimentary(), ct);

    public async Task<OverrideResponse> WaiveOutstandingAsync(WaiveOutstandingRequest r, string? ip, CancellationToken ct)
        => await ApplyAsync(r.SessionId, r.Reason, ip, r.DeviceInformation, "OutstandingWaived",
            s => s.WaiveOutstanding(), ct);

    public async Task<OverrideResponse> CorrectEntryTimeAsync(CorrectEntryTimeRequest r, string? ip, CancellationToken ct)
        => await ApplyAsync(r.SessionId, r.Reason, ip, r.DeviceInformation, "EntryTimeCorrected",
            s => s.CorrectEntryTime(r.NewEntryTime, _clock.UtcNow), ct);

    public async Task<OverrideResponse> CorrectPlateAsync(CorrectPlateRequest r, string? ip, CancellationToken ct)
    {
        var normalized = _plateNormalizer.Normalize(r.NewPlateNumber);
        if (string.IsNullOrEmpty(normalized))
            throw new ConflictException("Plate number is empty after normalization.");

        return await ApplyAsync(r.SessionId, r.Reason, ip, r.DeviceInformation, "PlateCorrected",
            async (s, ct2) =>
            {
                // Don't create a duplicate active session for the new plate at this location.
                var clash = await _db.ParkingSessions.AnyAsync(other =>
                    other.Id != s.Id &&
                    other.ParkingLocationId == s.ParkingLocationId &&
                    other.PlateNumberNormalized == normalized &&
                    (other.Status == ParkingSessionStatus.ActiveUnpaid ||
                     other.Status == ParkingSessionStatus.PaymentPending ||
                     other.Status == ParkingSessionStatus.PaidExitWindow ||
                     other.Status == ParkingSessionStatus.OverstayDue), ct2);
                if (clash)
                    throw new ConflictException("Another active session already uses that plate at this location.");

                s.CorrectPlate(r.NewPlateNumber, normalized);
            }, ct);
    }

    private Task<OverrideResponse> ApplyAsync(
        Guid sessionId, string reason, string? ip, string? device, string action,
        Action<ParkingSession> mutate, CancellationToken ct)
        => ApplyAsync(sessionId, reason, ip, device, action, (s, _) => { mutate(s); return Task.CompletedTask; }, ct);

    private async Task<OverrideResponse> ApplyAsync(
        Guid sessionId, string reason, string? ip, string? device, string action,
        Func<ParkingSession, CancellationToken, Task> mutate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ConflictException("A reason is required for this override.");

        var session = await _db.ParkingSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException("Parking session not found.");

        // Supervisors/admins may act on any location in their tenant.
        await GuardLocationAccess.EnsureCanOperateAsync(_db, _user, session.ParkingLocationId, ct);

        // Complimentary, waived, and voided sessions are no longer payable.
        // Close any existing online checkout before applying the override.
        if (action is "SessionVoided" or "MarkedComplimentary" or "OutstandingWaived")
            await _checkoutCleanup.CloseOpenCheckoutsAsync(session.Id, ct);

        var before = Snapshot(session);
        await mutate(session, ct);
        var after = Snapshot(session);

        await _audit.AddAsync(
            session.TenantId, session.ParkingLocationId, action,
            nameof(ParkingSession), session.Id.ToString(), before, after, reason,
            new AuditContext(ip, device), ct);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Override {Action} applied to session {SessionId} by {UserId}",
            action, session.Id, _user.UserId);

        await _realtime.SessionChangedAsync(
            session.TenantId, session.ParkingLocationId,
            new SessionRealtimeEvent(session.Id, session.ParkingLocationId, session.Status.ToString(),
                session.PlateNumberRaw, SessionEventKind.Overridden), ct);

        return new OverrideResponse(
            session.Id, action, session.Status.ToString(), session.PlateNumberRaw,
            session.FeeOverride, session.EntryTime);
    }

    private static object Snapshot(ParkingSession s) => new
    {
        Status = s.Status.ToString(),
        s.PlateNumberRaw,
        s.PlateNumberNormalized,
        s.EntryTime,
        s.FeeOverride,
        s.TotalPaid
    };
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Audit;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Pricing;
using ParkingSaaS.Contracts.Guard;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Application.Guard;

/// <summary>
/// Records a cash payment taken by a guard. Allowed only when the location has
/// cash enabled and the guard may operate there. Stored as a separate
/// <see cref="PaymentProvider.Cash"/> payment (never represented as PayMongo),
/// with a receipt number and change calculated against the server-computed
/// outstanding balance. Opens the paid-exit window like an online payment.
/// </summary>
public sealed class GuardCashPaymentService : IGuardCashPaymentService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly ISessionPricingService _pricing;
    private readonly IParkingTokenService _tokens;
    private readonly IAuditLogger _audit;
    private readonly IDateTime _clock;
    private readonly ISessionRealtimeNotifier _realtime;
    private readonly ILogger<GuardCashPaymentService> _logger;

    public GuardCashPaymentService(
        IApplicationDbContext db, ICurrentUser user, ISessionPricingService pricing,
        IParkingTokenService tokens, IAuditLogger audit, IDateTime clock,
        ISessionRealtimeNotifier realtime, ILogger<GuardCashPaymentService> logger)
    {
        _db = db;
        _user = user;
        _pricing = pricing;
        _tokens = tokens;
        _audit = audit;
        _clock = clock;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<CashReceiptResponse> RecordCashPaymentAsync(RecordCashPaymentRequest request, string? ipAddress, CancellationToken ct)
    {
        var session = await _db.ParkingSessions.FirstOrDefaultAsync(s => s.Id == request.SessionId, ct)
            ?? throw new NotFoundException("Parking session not found.");

        await GuardLocationAccess.EnsureCanOperateAsync(_db, _user, session.ParkingLocationId, ct);

        var now = _clock.UtcNow;
        session.RefreshTimeBasedStatus(now);
        if (!session.Status.IsActive())
            throw new ConflictException("This session is already closed.");

        var location = await _db.ParkingLocations.FirstAsync(l => l.Id == session.ParkingLocationId, ct);
        if (!location.AllowCashPayment)
            throw new ForbiddenException("Cash payments are disabled for this location.");

        var result = await _pricing.CalculateAsync(session, now, discount: null, ct);
        var currency = result?.Currency ?? "PHP";
        var amountDue = session.Outstanding(result?.TotalAmount ?? 0m);

        if (amountDue <= 0m)
            throw new ConflictException("No payment is due for this session.");
        if (request.AmountReceived < amountDue)
            throw new ConflictException("Amount received is less than the amount due.");

        var change = request.AmountReceived - amountDue;
        var receiptNumber = GenerateReceiptNumber(now);
        var reference = _tokens.GeneratePublicToken();

        var payment = Payment.CreateCashPaid(
            session.TenantId, session.Id, feeQuoteId: null, currency, amountDue,
            _tokens.Hash(reference), _tokens.Protect(reference), now, receiptNumber, _user.UserId ?? Guid.Empty);

        session.RegisterPayment(amountDue, now.AddMinutes(location.ExitGraceMinutes));

        await _db.Payments.AddAsync(payment, ct);
        await _audit.AddAsync(
            session.TenantId, session.ParkingLocationId, "CashPaymentRecorded",
            nameof(Payment), payment.Id.ToString(), oldValues: null,
            new { amountDue, request.AmountReceived, change, receiptNumber, session.TotalPaid },
            reason: null, new AuditContext(ipAddress, request.DeviceInformation), ct);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Cash payment {Receipt} ({AmountDue}) recorded for session {SessionId}",
            receiptNumber, amountDue, session.Id);

        await _realtime.SessionChangedAsync(
            session.TenantId, session.ParkingLocationId,
            new SessionRealtimeEvent(session.Id, session.ParkingLocationId, session.Status.ToString(),
                session.PlateNumberRaw, SessionEventKind.PaymentRecorded), ct);

        return new CashReceiptResponse(
            payment.Id, receiptNumber, amountDue, request.AmountReceived, change, currency,
            now, session.Status.ToString(), session.PaidExitDeadline);
    }

    private string GenerateReceiptNumber(DateTimeOffset now)
        => $"CR-{now:yyyyMMdd}-{_tokens.GenerateTicketCode()}";
}

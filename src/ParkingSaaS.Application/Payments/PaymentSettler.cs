using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Pricing;

namespace ParkingSaaS.Application.Payments;

/// <summary>
/// Settles a successful payment against its fee quote and parking session. Used
/// by both the webhook and reconciliation paths. Loads the related quote, session
/// and location through filter-bypassing queries (these run without a tenant in
/// context) and never overwrites an already-paid payment.
/// </summary>
public sealed class PaymentSettler : IPaymentSettler
{
    private readonly IApplicationDbContext _db;
    private readonly IEmailQueue _emailQueue;

    public PaymentSettler(IApplicationDbContext db, IEmailQueue emailQueue)
    {
        _db = db;
        _emailQueue = emailQueue;
    }

    public async Task<SettlementResult?> SettleAsync(
        Payment payment, string providerPaymentId, string? method, DateTimeOffset paidAt, CancellationToken ct)
    {
        if (payment.Status == PaymentStatus.Paid)
            return null; // already settled — preserve history, nothing to broadcast

        var session = await _db.ParkingSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == payment.ParkingSessionId, ct)
            ?? throw new InvalidOperationException($"Session {payment.ParkingSessionId} not found for payment {payment.Id}.");

        var location = await _db.ParkingLocations
            .IgnoreQueryFilters()
            .Where(l => l.Id == session.ParkingLocationId)
            .Select(l => new { l.ExitGraceMinutes, l.Name })
            .FirstOrDefaultAsync(ct);
        var graceMinutes = location?.ExitGraceMinutes ?? 0;

        payment.MarkPaid(providerPaymentId, method, paidAt);

        if (payment.FeeQuoteId is { } quoteId)
        {
            var quote = await _db.FeeQuotes.IgnoreQueryFilters().FirstOrDefaultAsync(q => q.Id == quoteId, ct);
            if (quote is { Status: FeeQuoteStatus.Active })
                quote.MarkUsed();
        }

        var deadline = paidAt.AddMinutes(graceMinutes);
        session.RegisterPayment(payment.Amount, deadline);

        // Queue the receipt if the customer left an email — staged on the same unit of work
        // as the settlement, so it commits atomically and the dispatcher sends it.
        if (!string.IsNullOrWhiteSpace(payment.CustomerEmail))
        {
            _emailQueue.QueuePaymentReceipt(
                payment.TenantId, payment.CustomerEmail!,
                new PaymentReceiptEmailData(
                    session.PlateNumberRaw, location?.Name ?? "Parking", payment.Amount, payment.Currency,
                    paidAt, method ?? payment.PaymentMethod, payment.Id.ToString("N")[..12], deadline),
                paidAt);
        }

        return new SettlementResult(
            session.TenantId, session.ParkingLocationId, session.Id,
            session.PlateNumberRaw, session.Status.ToString());
    }
}

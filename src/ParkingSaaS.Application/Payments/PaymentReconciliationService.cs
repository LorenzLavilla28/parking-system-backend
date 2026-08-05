using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Pricing;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Application.Payments;

/// <summary>
/// Polls the provider for the true status of open payments whose webhook may have
/// been delayed or lost, settling or failing them accordingly, and expires fee
/// quotes past their TTL. The webhook remains the primary path; this is the
/// safety net. Runs outside any tenant scope, so all queries bypass the filter.
/// </summary>
public sealed class PaymentReconciliationService : IPaymentReconciliationService
{
    private readonly IApplicationDbContext _db;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentSettler _settler;
    private readonly ISessionPricingService _pricing;
    private readonly IDateTime _clock;
    private readonly ISessionRealtimeNotifier _realtime;
    private readonly IEmailQueue? _emailQueue;
    private readonly IParkingTokenService _tokens;
    private readonly IQrCodeGenerator _qr;
    private readonly PublicUrlOptions _urls;
    private readonly PayMongoOptions _options;
    private readonly ILogger<PaymentReconciliationService> _logger;

    public PaymentReconciliationService(
        IApplicationDbContext db,
        IPaymentGateway gateway,
        IPaymentSettler settler,
        ISessionPricingService pricing,
        IDateTime clock,
        ISessionRealtimeNotifier realtime,
        IOptions<PayMongoOptions> options,
        ILogger<PaymentReconciliationService> logger,
        IEmailQueue? emailQueue,
        IParkingTokenService tokens,
        IQrCodeGenerator qr,
        IOptions<PublicUrlOptions> urls)
    {
        _db = db;
        _gateway = gateway;
        _settler = settler;
        _pricing = pricing;
        _clock = clock;
        _realtime = realtime;
        _options = options.Value;
        _logger = logger;
        _emailQueue = emailQueue;
        _tokens = tokens;
        _qr = qr;
        _urls = urls.Value;
    }

    public async Task<ReconciliationSummary> ReconcileAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var cutoff = now.AddMinutes(-_options.ReconcilePendingOlderThanMinutes);

        var open = await _db.Payments
            .IgnoreQueryFilters()
            .Where(p => p.Provider == PaymentProvider.PayMongo
                        && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing)
                        && p.ProviderCheckoutSessionId != null
                        && p.CreatedAt < cutoff)
            .OrderBy(p => p.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        int settled = 0, failed = 0;
        var settlements = new List<SettlementResult>();
        var abandoned = new List<Payment>();
        foreach (var payment in open)
        {
            try
            {
                var sessionStatus = await _db.ParkingSessions
                    .IgnoreQueryFilters()
                    .Where(s => s.Id == payment.ParkingSessionId)
                    .Select(s => (ParkingSessionStatus?)s.Status)
                    .FirstOrDefaultAsync(ct);
                var status = await _gateway.GetPaymentStatusAsync(payment.TenantId, payment.ProviderCheckoutSessionId!, ct);
                switch (status.Status)
                {
                    case PaymentStatus.Paid:
                        var result = await _settler.SettleAsync(payment, status.ProviderPaymentId ?? "reconciled", status.PaymentMethod, now, ct);
                        if (result is not null) settlements.Add(result);
                        settled++;
                        break;
                    case PaymentStatus.Expired:
                        payment.MarkExpired();
                        abandoned.Add(payment);
                        failed++;
                        break;
                    case PaymentStatus.Failed:
                        payment.MarkFailed();
                        abandoned.Add(payment);
                        failed++;
                        break;
                    case PaymentStatus.Pending:
                    case PaymentStatus.Processing:
                        if (sessionStatus is ParkingSessionStatus.Exited or ParkingSessionStatus.Void or ParkingSessionStatus.Cancelled)
                        {
                            // The session is already closed, so an old checkout must
                            // not remain payable indefinitely or inflate operations
                            // alerts. The provider status was read successfully and
                            // is not paid, so it is safe to expire and close locally.
                            await _gateway.ExpireCheckoutAsync(payment.TenantId, payment.ProviderCheckoutSessionId!, ct);
                            payment.Cancel();
                            abandoned.Add(payment);
                            failed++;
                        }
                        // Otherwise leave it open for the next sweep.
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconciliation failed for payment {PaymentId}.", payment.Id);
            }
        }

        // Free any session left in payment-pending by an abandoned checkout so the
        // customer can pay again — but only if no other checkout for it is still open.
        var reverts = await RevertAbandonedSessionsAsync(abandoned, ct);

        var quotesExpired = await ExpireStaleQuotesAsync(now, ct);
        var deadlineUpdates = await RefreshPaidExitDeadlinesAsync(now, ct);
        var overdue = await MarkOverdueSessionsAsync(now, ct);

        if (open.Count > 0 || quotesExpired > 0 || deadlineUpdates > 0 || overdue.Count > 0)
            await _db.SaveChangesAsync(ct);

        // Broadcast each settlement only after the batch has durably committed.
        foreach (var s in settlements)
            await _realtime.SessionChangedAsync(
                s.TenantId, s.ParkingLocationId,
                new SessionRealtimeEvent(s.SessionId, s.ParkingLocationId, s.Status, s.PlateNumberRaw,
                    SessionEventKind.PaymentRecorded), ct);

        // Tell guard/admin views a payment-pending session is unpaid again.
        foreach (var session in reverts)
            await _realtime.SessionChangedAsync(
                session.TenantId, session.ParkingLocationId,
                new SessionRealtimeEvent(session.Id, session.ParkingLocationId, session.Status.ToString(),
                    session.PlateNumberRaw, SessionEventKind.PaymentAbandoned), ct);

        foreach (var session in overdue)
            await _realtime.SessionChangedAsync(
                session.TenantId, session.ParkingLocationId,
                new SessionRealtimeEvent(session.Id, session.ParkingLocationId, session.Status.ToString(),
                    session.PlateNumberRaw, SessionEventKind.OverstayDue), ct);

        if (settled > 0 || failed > 0 || quotesExpired > 0 || deadlineUpdates > 0 || overdue.Count > 0)
            _logger.LogInformation("Reconcile: checked {Checked}, settled {Settled}, failed {Failed}, quotes expired {Quotes}, deadlines refreshed {Deadlines}, overstays marked {Overstays}.",
                open.Count, settled, failed, quotesExpired, deadlineUpdates, overdue.Count);

        return new ReconciliationSummary(open.Count, settled, failed, quotesExpired, overdue.Count);
    }

    private async Task<int> RefreshPaidExitDeadlinesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var sessions = await _db.ParkingSessions
            .IgnoreQueryFilters()
            .Where(s => (s.Status == ParkingSessionStatus.PaidExitWindow || s.Status == ParkingSessionStatus.OverstayDue)
                        && s.PaidExitDeadline != null)
            .Take(500)
            .ToListAsync(ct);

        var updated = 0;
        foreach (var session in sessions)
        {
            var paidAt = await _db.Payments
                .IgnoreQueryFilters()
                .Where(p => p.ParkingSessionId == session.Id
                            && p.Status == PaymentStatus.Paid
                            && p.PaidAt != null)
                .OrderByDescending(p => p.PaidAt)
                .Select(p => p.PaidAt)
                .FirstOrDefaultAsync(ct);
            if (paidAt is null)
                continue;

            var correctedDeadline = await _pricing.GetPaidExitDeadlineAsync(session, paidAt.Value, ct);
            if (session.PaidExitDeadline != correctedDeadline)
            {
                session.CorrectPaidExitDeadline(correctedDeadline);
                updated++;
            }

            var calculation = await _pricing.CalculateAsync(session, now, discount: null, ct);
            if (calculation is not null)
                session.RefreshTimeBasedStatus(now, calculation.TotalAmount);
        }

        return updated;
    }

    /// <summary>
    /// Reverts sessions to unpaid for checkouts that just expired/failed, so an abandoned
    /// online attempt no longer blocks a fresh one. A session is left as-is if it still has
    /// another open payment (a retry already in flight) so we never reopen a live checkout.
    /// </summary>
    private async Task<List<ParkingSession>> RevertAbandonedSessionsAsync(List<Payment> abandoned, CancellationToken ct)
    {
        var reverted = new List<ParkingSession>();
        if (abandoned.Count == 0) return reverted;

        var abandonedIds = abandoned.Select(p => p.Id).ToHashSet();
        var sessionIds = abandoned.Select(p => p.ParkingSessionId).Distinct().ToList();
        var sessions = await _db.ParkingSessions
            .IgnoreQueryFilters()
            .Where(s => sessionIds.Contains(s.Id) && s.Status == ParkingSessionStatus.PaymentPending)
            .ToListAsync(ct);

        foreach (var session in sessions)
        {
            // These payments are terminal in-memory but not yet saved, so exclude them by id;
            // any *other* open payment means a retry is already in flight — leave the session be.
            var stillOpen = await _db.Payments
                .IgnoreQueryFilters()
                .AnyAsync(p => p.ParkingSessionId == session.Id
                               && !abandonedIds.Contains(p.Id)
                               && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing), ct);
            if (stillOpen) continue;

            session.RevertToUnpaid();
            reverted.Add(session);
        }

        return reverted;
    }

    private async Task<int> ExpireStaleQuotesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var stale = await _db.FeeQuotes
            .IgnoreQueryFilters()
            .Where(q => q.Status == FeeQuoteStatus.Active && q.ExpiresAt <= now)
            .Take(200)
            .ToListAsync(ct);

        foreach (var quote in stale)
            quote.Expire();

        return stale.Count;
    }

    private async Task<List<ParkingSession>> MarkOverdueSessionsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var candidates = await _db.ParkingSessions
            .IgnoreQueryFilters()
            .Where(s => s.Status == ParkingSessionStatus.PaidExitWindow
                        && s.PaidExitDeadline != null
                        && s.PaidExitDeadline <= now)
            .OrderBy(s => s.PaidExitDeadline)
            .Take(500)
            .ToListAsync(ct);

        var overdue = new List<ParkingSession>();
        foreach (var session in candidates)
        {
            var calculation = await _pricing.CalculateAsync(session, now, discount: null, ct);
            if (calculation is null || session.Outstanding(calculation.TotalAmount) <= 0m)
                continue;

            if (_emailQueue is not null && session.PaidExitDeadline is { } deadline)
            {
                var recipient = await _db.Payments
                    .IgnoreQueryFilters()
                    .Where(p => p.ParkingSessionId == session.Id && p.CustomerEmail != null && p.Status == PaymentStatus.Paid)
                    .OrderByDescending(p => p.PaidAt ?? p.CreatedAt)
                    .Select(p => p.CustomerEmail)
                    .FirstOrDefaultAsync(ct);
                var locationName = await _db.ParkingLocations
                    .IgnoreQueryFilters()
                    .Where(l => l.Id == session.ParkingLocationId)
                    .Select(l => l.Name)
                    .FirstOrDefaultAsync(ct);
                if (!string.IsNullOrWhiteSpace(recipient))
                {
                    var paymentUrl = BuildPaymentUrl(session);
                    _emailQueue.QueueOverstayNotice(
                        session.TenantId, recipient!,
                        new OverstayNoticeEmailData(
                            session.PlateNumberRaw, locationName ?? "Parking", deadline,
                            paymentUrl,
                            string.IsNullOrWhiteSpace(paymentUrl) ? string.Empty : _qr.GeneratePngDataUri(paymentUrl)), now);
                }
            }
            session.MarkOverstay();
            overdue.Add(session);
        }

        return overdue;
    }

    private string BuildPaymentUrl(ParkingSession session)
    {
        try
        {
            return _urls.SessionPath(_tokens.Unprotect(session.PublicTokenProtected));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not rebuild payment URL for overdue session {SessionId}.", session.Id);
            return string.Empty;
        }
    }

}

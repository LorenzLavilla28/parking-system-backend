using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Payments;

namespace ParkingSaaS.Application.Payments;

/// <summary>
/// Processes PayMongo webhooks. The webhook is the source of truth for payment
/// success. Guarantees: the event is durably stored before being acted on; the
/// same event is never processed twice (unique (Provider, ProviderEventId) plus a
/// pre-check); amount/currency/session/tenant/quote are validated; and the
/// payment, fee quote, and session are updated together in a single transaction.
/// Successful payment history is never overwritten.
/// </summary>
public sealed class PaymentWebhookService : IPaymentWebhookService
{
    private readonly IApplicationDbContext _db;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentSettler _settler;
    private readonly IParkingTokenService _hasher;
    private readonly IDateTime _clock;
    private readonly ISessionRealtimeNotifier _realtime;
    private readonly ILogger<PaymentWebhookService> _logger;

    public PaymentWebhookService(
        IApplicationDbContext db,
        IPaymentGateway gateway,
        IPaymentSettler settler,
        IParkingTokenService hasher,
        IDateTime clock,
        ISessionRealtimeNotifier realtime,
        ILogger<PaymentWebhookService> logger)
    {
        _db = db;
        _gateway = gateway;
        _settler = settler;
        _hasher = hasher;
        _clock = clock;
        _realtime = realtime;
        _logger = logger;
    }

    public Task<WebhookOutcome> ProcessPayMongoAsync(string rawPayload, string signatureHeader, CancellationToken ct)
        => ProcessPayMongoAsync(rawPayload, signatureHeader, null, ct);

    public async Task<WebhookOutcome> ProcessPayMongoAsync(
        string rawPayload,
        string signatureHeader,
        string? webhookToken,
        CancellationToken ct)
    {
        Guid? tenantId = null;
        if (!string.IsNullOrWhiteSpace(webhookToken))
        {
            var tokenHash = _hasher.Hash(webhookToken);
            tenantId = await _db.TenantPayMongoConnections
                .IgnoreQueryFilters()
                .Where(c => c.WebhookTokenHash == tokenHash)
                .Select(c => (Guid?)c.TenantId)
                .FirstOrDefaultAsync(ct);

            if (tenantId is null)
                return WebhookOutcome.InvalidSignature;
        }

        var verification = tenantId is { } scopedTenant
            ? await _gateway.VerifyWebhookAsync(scopedTenant, rawPayload, signatureHeader, ct)
            : await _gateway.VerifyWebhookAsync(rawPayload, signatureHeader, ct);
        if (!verification.IsValid)
        {
            _logger.LogWarning("Rejected PayMongo webhook: invalid signature.");
            return WebhookOutcome.InvalidSignature;
        }

        if (string.IsNullOrWhiteSpace(verification.EventId))
        {
            _logger.LogWarning("PayMongo webhook missing event id; ignoring.");
            return WebhookOutcome.Ignored;
        }

        var now = _clock.UtcNow;

        // Idempotency pre-check: an already-stored event is a no-op.
        var alreadyStored = await _db.WebhookEvents
            .AnyAsync(e => e.Provider == PaymentProvider.PayMongo && e.ProviderEventId == verification.EventId, ct);
        if (alreadyStored)
        {
            _logger.LogInformation("Duplicate PayMongo event {EventId}; skipping.", verification.EventId);
            return WebhookOutcome.Duplicate;
        }

        var webhookEvent = new WebhookEvent(
            PaymentProvider.PayMongo, verification.EventId!, verification.EventType ?? "unknown",
            _hasher.Hash(rawPayload), now);
        if (tenantId is { } resolvedTenantId)
            webhookEvent.AssignTenant(resolvedTenantId);
        await _db.WebhookEvents.AddAsync(webhookEvent, ct);

        var (outcome, settlement) = await ApplyAsync(verification, webhookEvent, now, ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A concurrent delivery stored the same event first.
            _logger.LogInformation("Concurrent duplicate PayMongo event {EventId}.", verification.EventId);
            return WebhookOutcome.Duplicate;
        }

        // Broadcast only after the settlement has durably committed.
        if (settlement is { } s)
            await _realtime.SessionChangedAsync(
                s.TenantId, s.ParkingLocationId,
                new SessionRealtimeEvent(s.SessionId, s.ParkingLocationId, s.Status, s.PlateNumberRaw,
                    SessionEventKind.PaymentRecorded), ct);

        return outcome;
    }

    private async Task<(WebhookOutcome Outcome, SettlementResult? Settlement)> ApplyAsync(
        WebhookVerificationResult v, WebhookEvent webhookEvent, DateTimeOffset now, CancellationToken ct)
    {
        // Only act on paid events; store anything else for the record and ignore.
        if (v.MappedStatus != PaymentStatus.Paid)
        {
            webhookEvent.MarkIgnored(now, $"Unhandled event type '{v.EventType}'.");
            return (WebhookOutcome.Ignored, null);
        }

        if (string.IsNullOrWhiteSpace(v.ProviderCheckoutId))
        {
            webhookEvent.MarkIgnored(now, "No checkout id in event.");
            return (WebhookOutcome.Ignored, null);
        }

        var payment = await _db.Payments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.ProviderCheckoutSessionId == v.ProviderCheckoutId, ct);

        if (payment is null)
        {
            webhookEvent.MarkIgnored(now, "No matching payment for checkout.");
            return (WebhookOutcome.Ignored, null);
        }

        if (webhookEvent.TenantId is { } webhookTenantId && payment.TenantId != webhookTenantId)
        {
            webhookEvent.MarkIgnored(now, "Payment belongs to a different tenant.");
            return (WebhookOutcome.Ignored, null);
        }

        webhookEvent.LinkPayment(payment.Id);

        // Validate currency and amount against our record — never trust the event blindly.
        if (!string.Equals(v.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase) ||
            (v.Amount is { } amt && amt != payment.Amount))
        {
            _logger.LogError("PayMongo webhook amount/currency mismatch for payment {PaymentId}.", payment.Id);
            webhookEvent.MarkFailed(now, "Amount or currency mismatch.");
            payment.MarkFailed();
            return (WebhookOutcome.Processed, null); // stored & acked; do not retry a mismatch
        }

        if (payment.Status == PaymentStatus.Paid)
        {
            // The payment was already settled (e.g. reconciliation got there first).
            webhookEvent.MarkProcessed(now, payment.TenantId);
            return (WebhookOutcome.Processed, null);
        }

        // Settle: mark paid, consume the quote, open the paid exit window — all
        // staged here and committed atomically with the event by the single SaveChanges.
        SettlementResult? settlement;
        try
        {
            settlement = await _settler.SettleAsync(payment, v.ProviderPaymentId ?? "unknown", v.PaymentMethod, now, ct);
        }
        catch (Exception ex)
        {
            webhookEvent.MarkFailed(now, ex.Message);
            _logger.LogError(ex, "Failed to settle payment {PaymentId} from webhook.", payment.Id);
            return (WebhookOutcome.Processed, null); // stored & acked; reconciliation will retry settlement
        }

        webhookEvent.MarkProcessed(now, payment.TenantId);
        _logger.LogInformation("Payment {PaymentId} settled via webhook; session {SessionId} in paid exit window.",
            payment.Id, payment.ParkingSessionId);
        return (WebhookOutcome.Processed, settlement);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException?.GetType().Name == "PostgresException"
           && (ex.InnerException as dynamic)?.SqlState == "23505";
}

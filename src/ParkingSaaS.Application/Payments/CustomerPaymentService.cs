using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Audit;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Contracts.Customer;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Application.Payments;

/// <summary>
/// Creates PayMongo hosted-checkout sessions from immutable fee quotes and serves
/// status-page polling. Idempotent on a quote: a second checkout request for the
/// same active quote returns the existing pending checkout rather than charging
/// twice. The amount always comes from the quote, never from the client.
///
/// The status endpoint is self-healing: when polled it confirms an open payment
/// directly against PayMongo and settles it on the spot, so the customer sees
/// "Paid" within a poll even if the webhook is delayed or not configured. The
/// webhook remains the authoritative/fast path; reconciliation is the background net.
/// </summary>
public sealed class CustomerPaymentService : ICustomerPaymentService
{
    private readonly IApplicationDbContext _db;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentSettler _settler;
    private readonly IParkingTokenService _tokens;
    private readonly IPayMongoCredentialsResolver _payMongoCredentials;
    private readonly IDateTime _clock;
    private readonly ISessionRealtimeNotifier _realtime;
    private readonly IAuditLogger? _audit;
    private readonly PublicUrlOptions _urls;
    private readonly ILogger<CustomerPaymentService> _logger;

    public CustomerPaymentService(
        IApplicationDbContext db,
        IPaymentGateway gateway,
        IPaymentSettler settler,
        IParkingTokenService tokens,
        IPayMongoCredentialsResolver payMongoCredentials,
        IDateTime clock,
        ISessionRealtimeNotifier realtime,
        IOptions<PublicUrlOptions> urls,
        ILogger<CustomerPaymentService> logger,
        IAuditLogger? audit = null)
    {
        _db = db;
        _gateway = gateway;
        _settler = settler;
        _tokens = tokens;
        _payMongoCredentials = payMongoCredentials;
        _clock = clock;
        _realtime = realtime;
        _urls = urls.Value;
        _audit = audit;
        _logger = logger;
    }

    public async Task<CheckoutResponse> CreateCheckoutAsync(StartCheckoutRequest request, CancellationToken ct)
        => await CreateCheckoutAsync(request, null, null, ct);

    public async Task<CheckoutResponse> CreateCheckoutAsync(
        StartCheckoutRequest request, string? ipAddress, string? deviceInformation, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        var quote = await _db.FeeQuotes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(q => q.Id == request.FeeQuoteId, ct)
            ?? throw new NotFoundException("Fee quote not found.");

        if (!quote.IsActive(now))
            throw new ConflictException("This fee quote has expired. Please refresh the fee and try again.");
        if (quote.TotalAmount <= 0m)
            throw new ConflictException("No payment is required for this session.");

        if (await _payMongoCredentials.ResolveAsync(quote.TenantId, ct) is null)
            throw new ConflictException(
                "Online PayMongo payments are not configured for this tenant. Please pay at the parking attendant.");

        var session = await _db.ParkingSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == quote.ParkingSessionId, ct)
            ?? throw new NotFoundException("Parking session not found.");

        // Idempotency: reuse an existing open checkout for this quote.
        var existing = await _db.Payments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.FeeQuoteId == quote.Id && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing), ct);

        if (existing is { ProviderCheckoutUrl: not null and not "" })
        {
            var existingReference = _tokens.Unprotect(existing.PublicReferenceProtected);
            return new CheckoutResponse(existingReference, existing.ProviderCheckoutUrl, existing.Amount, existing.Currency);
        }

        // Starting a fresh checkout (typically after the customer abandoned or cancelled a
        // previous attempt). Reconcile every earlier open checkout for this session FIRST so
        // we never leave an orphaned still-payable checkout (double-charge) AND never cancel
        // one the customer actually completed — we verify each at PayMongo before releasing it.
        await SupersedeOpenCheckoutsAsync(session.Id, now, ct);

        var reference = _tokens.GeneratePublicToken();
        var idempotencyKey = $"quote-{quote.Id:N}";

        var payment = Payment.CreateOnlinePending(
            quote.TenantId, session.Id, quote.Id, quote.Currency, quote.TotalAmount,
            _tokens.Hash(reference), _tokens.Protect(reference), idempotencyKey,
            customerEmail: request.Email);

        var checkout = await _gateway.CreateCheckoutAsync(payment.TenantId, new CreateCheckoutRequest(
            Currency: quote.Currency,
            Amount: quote.TotalAmount,
            Description: $"Parking payment ({session.PlateNumberRaw})",
            LineItemName: "Parking fee",
            ReferenceNumber: payment.Id.ToString("N"),
            SuccessUrl: _urls.PaymentStatusPath(reference),
            CancelUrl: _urls.SessionPath(_tokens.Unprotect(session.PublicTokenProtected)),
            IdempotencyKey: idempotencyKey,
            CustomerEmail: request.Email), ct);

        payment.SetCheckoutSession(checkout.ProviderCheckoutId, checkout.CheckoutUrl);
        payment.SetProviderAccountId(checkout.ProviderAccountId);
        session.MarkPaymentPending();

        await _db.Payments.AddAsync(payment, ct);
        if (_audit is not null)
        {
            await _audit.AddAsync(
                payment.TenantId, session.ParkingLocationId, "OnlineCheckoutCreated",
                nameof(Payment), payment.Id.ToString(), oldValues: null,
                new
                {
                    payment.Provider,
                    payment.Amount,
                    payment.Currency,
                    payment.Status,
                    payment.FeeQuoteId,
                    payment.CustomerEmail,
                    payment.ProviderCheckoutSessionId
                },
                reason: null, new AuditContext(ipAddress, deviceInformation), ct);
        }
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Checkout {CheckoutId} created for session {SessionId} ({Amount} {Currency})",
            checkout.ProviderCheckoutId, session.Id, quote.TotalAmount, quote.Currency);

        return new CheckoutResponse(reference, checkout.CheckoutUrl, payment.Amount, payment.Currency);
    }

    public async Task<PaymentStatusResponse> GetStatusAsync(string paymentReference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(paymentReference))
            throw new NotFoundException("Payment not found.");

        var hash = _tokens.Hash(paymentReference);
        var payment = await _db.Payments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.PublicReferenceHash == hash, ct)
            ?? throw new NotFoundException("Payment not found.");

        // Self-healing: confirm an open payment directly at PayMongo and settle/close it now,
        // so the status page reflects a completed payment without waiting for the webhook or
        // the background reconciliation sweep.
        if (payment.IsOpen && payment.Provider == PaymentProvider.PayMongo)
            await ConfirmOpenPaymentAsync(payment, _clock.UtcNow, ct);

        DateTimeOffset? deadline = null;
        if (payment.Status == PaymentStatus.Paid)
        {
            deadline = await _db.ParkingSessions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.Id == payment.ParkingSessionId)
                .Select(s => s.PaidExitDeadline)
                .FirstOrDefaultAsync(ct);
        }

        return new PaymentStatusResponse(
            payment.Status.ToString(), payment.Amount, payment.Currency, payment.PaidAt, deadline);
    }

    /// <summary>
    /// Releases prior open checkouts for a session before a new one is created. Each is
    /// checked at PayMongo: if the customer already paid it, it is settled (never cancelled)
    /// and the caller is stopped so no second charge is opened; an unpaid one is expired and
    /// cancelled; one whose status can't be read is left untouched (safe default).
    /// </summary>
    private async Task SupersedeOpenCheckoutsAsync(Guid sessionId, DateTimeOffset now, CancellationToken ct)
    {
        var priorOpen = await _db.Payments
            .IgnoreQueryFilters()
            .Where(p => p.ParkingSessionId == sessionId
                        && p.Provider == PaymentProvider.PayMongo
                        && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing))
            .ToListAsync(ct);

        foreach (var prior in priorOpen)
        {
            if (prior.ProviderCheckoutSessionId is not { Length: > 0 } checkoutId)
            {
                prior.Cancel();
                continue;
            }

            PaymentStatusResult providerStatus;
            try
            {
                providerStatus = await _gateway.GetPaymentStatusAsync(prior.TenantId, checkoutId, ct);
            }
            catch (Exception ex)
            {
                // Can't verify → don't risk cancelling a paid checkout; leave it for the
                // status poll / reconciliation to resolve.
                _logger.LogWarning(ex, "Could not verify prior checkout {CheckoutId}; leaving it open.", checkoutId);
                continue;
            }

            if (providerStatus.Status == PaymentStatus.Paid)
            {
                var settlement = await _settler.SettleAsync(prior, providerStatus.ProviderPaymentId ?? "reconciled", providerStatus.PaymentMethod, now, ct);
                await _db.SaveChangesAsync(ct);
                await BroadcastAsync(settlement, SessionEventKind.PaymentRecorded, ct);
                throw new ConflictException("This session has already been paid.");
            }

            // Genuinely unpaid → safe to release the old checkout before starting a new one.
            try
            {
                await _gateway.ExpireCheckoutAsync(prior.TenantId, checkoutId, ct);
                prior.Cancel();
            }
            catch (Exception ex)
            {
                // Do not mark the local attempt cancelled when the provider could
                // not confirm that the checkout was safely expired.
                _logger.LogWarning(ex, "Failed to expire superseded checkout {CheckoutId}; leaving it open.", checkoutId);
            }
        }
    }

    /// <summary>Confirms a single open payment against PayMongo, settling or closing it in place.</summary>
    private async Task ConfirmOpenPaymentAsync(Payment payment, DateTimeOffset now, CancellationToken ct)
    {
        if (payment.ProviderCheckoutSessionId is not { Length: > 0 } checkoutId)
            return;

        PaymentStatusResult status;
        try
        {
            status = await _gateway.GetPaymentStatusAsync(payment.TenantId, checkoutId, ct);
        }
        catch (Exception ex)
        {
            // Provider unreachable — return the stored status; the next poll retries.
            _logger.LogWarning(ex, "Live status check failed for payment {PaymentId}.", payment.Id);
            return;
        }

        if (status.Status == PaymentStatus.Paid)
        {
            var settlement = await _settler.SettleAsync(payment, status.ProviderPaymentId ?? "reconciled", status.PaymentMethod, now, ct);
            await _db.SaveChangesAsync(ct);
            await BroadcastAsync(settlement, SessionEventKind.PaymentRecorded, ct);
        }
        else if (status.Status is PaymentStatus.Expired or PaymentStatus.Failed)
        {
            if (status.Status == PaymentStatus.Expired) payment.MarkExpired();
            else payment.MarkFailed();

            var reverted = await RevertSessionIfAbandonedAsync(payment, ct);
            await _db.SaveChangesAsync(ct);
            if (reverted is { } session)
                await _realtime.SessionChangedAsync(
                    session.TenantId, session.ParkingLocationId,
                    new SessionRealtimeEvent(session.Id, session.ParkingLocationId, session.Status.ToString(),
                        session.PlateNumberRaw, SessionEventKind.PaymentAbandoned), ct);
        }
    }

    /// <summary>Frees a payment-pending session once its last open checkout is closed.</summary>
    private async Task<ParkingSession?> RevertSessionIfAbandonedAsync(Payment payment, CancellationToken ct)
    {
        var session = await _db.ParkingSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == payment.ParkingSessionId, ct);
        if (session is null || session.Status != ParkingSessionStatus.PaymentPending)
            return null;

        // Exclude this payment (terminal in-memory but not yet saved); any *other* open
        // payment means a retry is in flight, so leave the session as-is.
        var otherOpen = await _db.Payments
            .IgnoreQueryFilters()
            .AnyAsync(p => p.ParkingSessionId == session.Id
                           && p.Id != payment.Id
                           && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing), ct);
        if (otherOpen) return null;

        session.RevertToUnpaid();
        return session;
    }

    private async Task BroadcastAsync(SettlementResult? settlement, string kind, CancellationToken ct)
    {
        if (settlement is not { } s) return;
        await _realtime.SessionChangedAsync(
            s.TenantId, s.ParkingLocationId,
            new SessionRealtimeEvent(s.SessionId, s.ParkingLocationId, s.Status, s.PlateNumberRaw, kind), ct);
    }
}

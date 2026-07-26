using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Application.Pricing;
using ParkingSaaS.Contracts.Customer;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Application.Customer;

/// <summary>
/// Customer-facing fee display and quote creation. The fee is always computed on
/// the backend; the amount the customer pays comes from a persisted, immutable
/// <see cref="FeeQuote"/> — never from a value supplied by the client.
/// </summary>
public sealed class CustomerPricingService : ICustomerPricingService
{
    private readonly IApplicationDbContext _db;
    private readonly IParkingTokenService _tokens;
    private readonly ISessionPricingService _pricing;
    private readonly IPayMongoCredentialsResolver _payMongoCredentials;
    private readonly IDateTime _clock;
    private readonly PricingOptions _options;
    private readonly ILogger<CustomerPricingService> _logger;

    public CustomerPricingService(
        IApplicationDbContext db,
        IParkingTokenService tokens,
        ISessionPricingService pricing,
        IPayMongoCredentialsResolver payMongoCredentials,
        IDateTime clock,
        IOptions<PricingOptions> options,
        ILogger<CustomerPricingService> logger)
    {
        _db = db;
        _tokens = tokens;
        _pricing = pricing;
        _payMongoCredentials = payMongoCredentials;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CurrentFeeResponse> GetCurrentFeeAsync(string publicToken, CancellationToken ct)
    {
        var session = await ResolveSessionAsync(publicToken, ct);
        var now = _clock.UtcNow;
        var result = await _pricing.CalculateAsync(session, now, discount: null, ct);
        var paymentAvailability = await GetPaymentAvailabilityAsync(session, ct);

        if (result is null)
        {
            return new CurrentFeeResponse(
                PricingAvailable: false, "PHP", 0m, 0m, 0m, 0m, 0m,
                session.EntryTime, now, Array.Empty<FeeBreakdownItem>(),
                paymentAvailability.Online, paymentAvailability.Cash);
        }

        return new CurrentFeeResponse(
            PricingAvailable: true,
            result.Currency,
            result.BaseAmount,
            result.AdditionalAmount,
            result.DiscountAmount,
            result.TotalAmount,
            session.Outstanding(result.TotalAmount),
            result.EntryTime,
            result.CalculationTime,
            result.Breakdown.ToContract(),
            paymentAvailability.Online,
            paymentAvailability.Cash);
    }

    public async Task<FeeQuoteResponse> CreateQuoteAsync(CreateFeeQuoteRequest request, CancellationToken ct)
    {
        var session = await ResolveSessionAsync(request.PublicToken, ct);
        var now = _clock.UtcNow;
        

        // Recalculate at quote time — the displayed amount is never trusted.
        var result = await _pricing.CalculateAsync(session, now, discount: null, ct)
            ?? throw new ConflictException("Pricing is not available for this session.");
        session.RefreshTimeBasedStatus(now, result.TotalAmount);

        if (!session.Status.IsActive())
            throw new ConflictException("This parking session is not awaiting payment.");

        // Charge only the outstanding balance (handles overstay top-ups and fee
        // overrides). Earlier successful payments are preserved.
        var outstanding = session.Outstanding(result.TotalAmount);
        if (outstanding <= 0m)
            throw new ConflictException("No payment is currently due for this session.");

        var breakdown = result.Breakdown.ToContract().ToList();
        if (session.TotalPaid > 0m)
            breakdown.Add(new FeeBreakdownItem("already_paid", "Already paid", -session.TotalPaid));

        var subtotal = result.BaseAmount + result.AdditionalAmount;
        var quote = new FeeQuote(
            tenantId: session.TenantId,
            parkingSessionId: session.Id,
            currency: result.Currency,
            baseAmount: subtotal,
            discountAmount: result.DiscountAmount,
            totalAmount: outstanding,
            createdAt: now,
            expiresAt: now.AddMinutes(_options.FeeQuoteMinutes),
            pricingBreakdownJson: System.Text.Json.JsonSerializer.Serialize(breakdown),
            ratePlanVersionId: result.RatePlanVersionId);

        await _db.FeeQuotes.AddAsync(quote, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Fee quote {QuoteId} created for session {SessionId} ({Total} {Currency})",
            quote.Id, session.Id, quote.TotalAmount, quote.Currency);

        return new FeeQuoteResponse(
            quote.Id, quote.Currency, quote.TotalAmount, quote.CreatedAt, quote.ExpiresAt,
            quote.Status.ToString(), breakdown);
    }

    private async Task<ParkingSession> ResolveSessionAsync(string publicToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(publicToken))
            throw new NotFoundException("Session not found.");

        var hash = _tokens.Hash(publicToken);
        return await _db.ParkingSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.PublicTokenHash == hash, ct)
            ?? throw new NotFoundException("Session not found.");
    }

    private async Task<(bool Online, bool Cash)> GetPaymentAvailabilityAsync(
        ParkingSession session, CancellationToken ct)
    {
        var online = await _payMongoCredentials.ResolveAsync(session.TenantId, ct) is not null;
        var cash = await _db.ParkingLocations
            .IgnoreQueryFilters()
            .Where(l => l.Id == session.ParkingLocationId)
            .Select(l => l.AllowCashPayment)
            .FirstOrDefaultAsync(ct);

        return (online, cash);
    }
}

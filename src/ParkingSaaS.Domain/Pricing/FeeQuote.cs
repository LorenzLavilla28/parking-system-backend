using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Pricing;

public enum FeeQuoteStatus
{
    Active = 1,
    Expired = 2,
    Used = 3,
    Cancelled = 4
}

/// <summary>
/// A short-lived, immutable snapshot of a calculated fee. The customer pays
/// against a quote, never an amount supplied by the client. Once consumed by a
/// successful payment it is marked <see cref="FeeQuoteStatus.Used"/>.
/// </summary>
public class FeeQuote : AggregateRoot, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ParkingSessionId { get; private set; }
    public string Currency { get; private set; } = "PHP";
    public decimal BaseAmount { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public FeeQuoteStatus Status { get; private set; } = FeeQuoteStatus.Active;
    public string PricingBreakdownJson { get; private set; } = "[]";
    public Guid? RatePlanVersionId { get; private set; }

    private FeeQuote() { }

    public FeeQuote(
        Guid tenantId, Guid parkingSessionId, string currency,
        decimal baseAmount, decimal discountAmount, decimal totalAmount,
        DateTimeOffset createdAt, DateTimeOffset expiresAt,
        string pricingBreakdownJson, Guid? ratePlanVersionId)
    {
        if (totalAmount < 0m) throw new DomainException("feequote.total_negative", "Total cannot be negative.");
        TenantId = tenantId;
        ParkingSessionId = parkingSessionId;
        Currency = currency;
        BaseAmount = baseAmount;
        DiscountAmount = discountAmount;
        TotalAmount = totalAmount;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        PricingBreakdownJson = pricingBreakdownJson;
        RatePlanVersionId = ratePlanVersionId;
        Status = FeeQuoteStatus.Active;
    }

    public bool IsActive(DateTimeOffset now) => Status == FeeQuoteStatus.Active && ExpiresAt > now;

    public void MarkUsed()
    {
        if (Status != FeeQuoteStatus.Active)
            throw new DomainException("feequote.not_active", "Only an active quote can be used.");
        Status = FeeQuoteStatus.Used;
    }

    public void Expire()
    {
        if (Status == FeeQuoteStatus.Active)
            Status = FeeQuoteStatus.Expired;
    }

    public void Cancel()
    {
        if (Status == FeeQuoteStatus.Active)
            Status = FeeQuoteStatus.Cancelled;
    }
}

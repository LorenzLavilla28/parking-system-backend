using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Domain.Pricing;

/// <summary>A discount to apply to a calculation. Validation/authorization happens at the use-case layer.</summary>
public sealed record DiscountInput(DiscountType Type, decimal Value, bool IsPercentage, string? Reference = null)
{
    public static readonly DiscountInput None = new(DiscountType.None, 0m, false);
}

/// <summary>Everything the calculator needs. Pure data — no I/O.</summary>
public sealed record FeeCalculationInput(
    DateTimeOffset EntryTime,
    DateTimeOffset CalculationTime,
    VehicleType VehicleType,
    Guid RatePlanVersionId,
    int RatePlanVersionNumber,
    PricingRules Rules,
    string Timezone,
    DiscountInput? Discount = null);

/// <summary>One labelled component of the price, for transparent breakdowns.</summary>
public sealed record PricingLineItem(string Code, string Description, decimal Amount);

/// <summary>The immutable result of a fee calculation.</summary>
public sealed record FeeCalculationResult(
    DateTimeOffset EntryTime,
    DateTimeOffset CalculationTime,
    string Currency,
    decimal BaseAmount,
    decimal AdditionalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    IReadOnlyList<PricingLineItem> Breakdown,
    Guid RatePlanVersionId,
    int RatePlanVersionNumber);

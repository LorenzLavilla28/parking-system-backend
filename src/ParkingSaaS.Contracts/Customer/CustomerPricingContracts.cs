namespace ParkingSaaS.Contracts.Customer;

public sealed record FeeBreakdownItem(string Code, string Description, decimal Amount);

/// <summary>The current calculated fee for a session, with a transparent breakdown.</summary>
public sealed record CurrentFeeResponse(
    bool PricingAvailable,
    string Currency,
    decimal BaseAmount,
    decimal AdditionalAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    decimal Outstanding,
    DateTimeOffset EntryTime,
    DateTimeOffset CalculationTime,
    IReadOnlyList<FeeBreakdownItem> Breakdown,
    bool OnlinePaymentAvailable = false,
    bool CashPaymentAvailable = false);

public sealed record CreateFeeQuoteRequest(string PublicToken);

/// <summary>A short-lived, immutable quote the customer pays against.</summary>
public sealed record FeeQuoteResponse(
    Guid Id,
    string Currency,
    decimal TotalAmount,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    IReadOnlyList<FeeBreakdownItem> Breakdown);

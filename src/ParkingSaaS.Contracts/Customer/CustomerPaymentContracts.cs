namespace ParkingSaaS.Contracts.Customer;

/// <summary>Starts an online payment for a previously-created fee quote.</summary>
public sealed record StartCheckoutRequest(Guid FeeQuoteId, string? Email);

public sealed record CheckoutResponse(
    string PaymentReference,
    string CheckoutUrl,
    decimal Amount,
    string Currency,
    string? QrCodeImageUrl = null,
    DateTimeOffset? ExpiresAt = null);

/// <summary>Polled by the success page until the backend confirms the payment.</summary>
public sealed record PaymentStatusResponse(
    string Status,
    decimal Amount,
    string Currency,
    DateTimeOffset? PaidAt,
    DateTimeOffset? PaidExitDeadline);

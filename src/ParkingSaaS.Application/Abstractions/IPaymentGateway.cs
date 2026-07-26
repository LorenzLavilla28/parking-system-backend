using ParkingSaaS.Domain.Payments;

namespace ParkingSaaS.Application.Abstractions;

public sealed record CreateCheckoutRequest(
    string Currency,
    decimal Amount,
    string Description,
    string LineItemName,
    string ReferenceNumber,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    string? CustomerEmail = null);

public sealed record CreateCheckoutResult(
    string ProviderCheckoutId,
    string CheckoutUrl,
    string RawProviderReference,
    string? ProviderAccountId = null);

public sealed record PaymentStatusResult(
    PaymentStatus Status,
    string? ProviderPaymentId,
    decimal? AmountPaid,
    string? Currency,
    string? PaymentMethod);

/// <summary>Normalized result of verifying and parsing a provider webhook.</summary>
public sealed record WebhookVerificationResult(
    bool IsValid,
    string? EventId,
    string? EventType,
    string? ProviderCheckoutId,
    string? ProviderPaymentId,
    PaymentStatus? MappedStatus,
    decimal? Amount,
    string? Currency,
    string? PaymentMethod);

/// <summary>
/// Provider-agnostic payment operations. The concrete implementation
/// (PayMongo) lives in infrastructure and is the only place the secret key is
/// used; nothing above this abstraction knows the provider's wire format.
/// </summary>
public interface IPaymentGateway
{
    Task<CreateCheckoutResult> CreateCheckoutAsync(CreateCheckoutRequest request, CancellationToken cancellationToken);
    Task<CreateCheckoutResult> CreateCheckoutAsync(Guid tenantId, CreateCheckoutRequest request, CancellationToken cancellationToken);
    Task<PaymentStatusResult> GetPaymentStatusAsync(string providerReference, CancellationToken cancellationToken);
    Task<PaymentStatusResult> GetPaymentStatusAsync(Guid tenantId, string providerReference, CancellationToken cancellationToken);
    Task ExpireCheckoutAsync(string providerReference, CancellationToken cancellationToken);
    Task ExpireCheckoutAsync(Guid tenantId, string providerReference, CancellationToken cancellationToken);
    Task<WebhookVerificationResult> VerifyWebhookAsync(string rawPayload, string signatureHeader, CancellationToken cancellationToken);
    Task<WebhookVerificationResult> VerifyWebhookAsync(Guid tenantId, string rawPayload, string signatureHeader, CancellationToken cancellationToken);
}

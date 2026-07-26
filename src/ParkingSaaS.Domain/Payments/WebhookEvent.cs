using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Payments;

/// <summary>
/// A provider webhook event, stored durably so the same event is never processed
/// twice. Provider-global (not tenant-owned); the resolved tenant is recorded
/// after processing. A unique constraint on (Provider, ProviderEventId) is the
/// authoritative idempotency guard.
/// </summary>
public class WebhookEvent : AggregateRoot
{
    public PaymentProvider Provider { get; private set; }
    public string ProviderEventId { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string PayloadHash { get; private set; } = string.Empty;
    public Guid? PaymentId { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public WebhookProcessingStatus ProcessingStatus { get; private set; } = WebhookProcessingStatus.Received;
    public string? FailureReason { get; private set; }
    public Guid? TenantId { get; private set; }

    private WebhookEvent() { }

    public WebhookEvent(PaymentProvider provider, string providerEventId, string eventType, string payloadHash, DateTimeOffset receivedAt)
    {
        Provider = provider;
        ProviderEventId = providerEventId;
        EventType = eventType;
        PayloadHash = payloadHash;
        ReceivedAt = receivedAt;
        ProcessingStatus = WebhookProcessingStatus.Received;
    }

    public void MarkProcessed(DateTimeOffset at, Guid? tenantId)
    {
        ProcessingStatus = WebhookProcessingStatus.Processed;
        ProcessedAt = at;
        TenantId = tenantId;
    }

    public void LinkPayment(Guid paymentId) => PaymentId = paymentId;

    public void AssignTenant(Guid tenantId) => TenantId = tenantId;

    public void MarkIgnored(DateTimeOffset at, string reason)
    {
        ProcessingStatus = WebhookProcessingStatus.Ignored;
        ProcessedAt = at;
        FailureReason = reason;
    }

    public void MarkFailed(DateTimeOffset at, string reason)
    {
        ProcessingStatus = WebhookProcessingStatus.Failed;
        ProcessedAt = at;
        FailureReason = Truncate(reason, 1000);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

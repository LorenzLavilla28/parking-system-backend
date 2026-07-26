using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Payments;

public enum PayMongoConnectionStatus
{
    NotConnected,
    Connected,
    Invalid,
    Disconnected
}

/// <summary>
/// Stores non-secret routing metadata for a tenant-owned PayMongo account.
/// Credentials live in AWS Secrets Manager; this entity stores only the secret
/// reference and an opaque webhook route token.
/// </summary>
public sealed class TenantPayMongoConnection : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Environment { get; private set; } = "test";
    public string? PayMongoAccountId { get; private set; }
    public string SecretArn { get; private set; } = string.Empty;
    public string WebhookTokenHash { get; private set; } = string.Empty;
    public string WebhookTokenProtected { get; private set; } = string.Empty;
    public PayMongoConnectionStatus Status { get; private set; } = PayMongoConnectionStatus.NotConnected;
    public DateTimeOffset? LastValidatedAt { get; private set; }
    public string? LastError { get; private set; }

    private TenantPayMongoConnection() { }

    public TenantPayMongoConnection(
        Guid tenantId,
        string environment,
        string secretArn,
        string webhookTokenHash,
        string webhookTokenProtected)
    {
        TenantId = tenantId;
        Environment = NormalizeEnvironment(environment);
        SecretArn = secretArn;
        WebhookTokenHash = webhookTokenHash;
        WebhookTokenProtected = webhookTokenProtected;
    }

    public void MarkConnected(string? accountId, DateTimeOffset validatedAt)
    {
        PayMongoAccountId = string.IsNullOrWhiteSpace(accountId) ? null : accountId.Trim();
        Status = PayMongoConnectionStatus.Connected;
        LastValidatedAt = validatedAt;
        LastError = null;
    }

    public void MarkInvalid(string reason, DateTimeOffset at)
    {
        Status = PayMongoConnectionStatus.Invalid;
        LastValidatedAt = at;
        LastError = reason.Length <= 500 ? reason : reason[..500];
    }

    public void MarkDisconnected(DateTimeOffset at)
    {
        Status = PayMongoConnectionStatus.Disconnected;
        LastValidatedAt = at;
        LastError = null;
    }

    public static string NormalizeEnvironment(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "test" or "sandbox" => "test",
            "live" or "production" => "live",
            _ => throw new DomainException("paymongo.environment_invalid", "PayMongo environment must be test or live.")
        };
}

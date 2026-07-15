using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Users;

/// <summary>
/// A single-use password reset token. Only its SHA-256 hash is persisted.
/// </summary>
public sealed class PasswordResetToken : Entity, ITenantOwned
{
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    private PasswordResetToken() { }

    public PasswordResetToken(Guid userId, Guid tenantId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        UserId = userId;
        TenantId = tenantId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public bool IsActive(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;

    public void Consume(DateTimeOffset now) => UsedAt = now;
}

using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Users;

/// <summary>
/// A rotating refresh token. Only the SHA-256 hash of the token value is
/// stored, so a database leak does not yield usable tokens. Rotation is
/// modelled via <see cref="ReplacedByTokenHash"/>.
/// </summary>
public class RefreshToken : Entity
{
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }
    public string? CreatedByIp { get; private set; }

    private RefreshToken() { }

    public RefreshToken(Guid userId, Guid tenantId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, string? createdByIp)
    {
        UserId = userId;
        TenantId = tenantId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        CreatedByIp = createdByIp;
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTimeOffset now, string? replacedByTokenHash = null)
    {
        RevokedAt = now;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}

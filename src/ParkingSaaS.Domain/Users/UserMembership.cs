using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Users;

/// <summary>
/// A scoped access membership for an account. A tenant membership uses the
/// tenant id; the platform membership uses <see cref="Guid.Empty"/>.
/// </summary>
public sealed class UserMembership : Entity, ITenantOwned
{
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public MembershipStatus Status { get; private set; }

    private UserMembership() { }

    public UserMembership(Guid userId, Guid tenantId)
    {
        UserId = userId;
        TenantId = tenantId;
        Status = MembershipStatus.Active;
    }

    public void Disable() => Status = MembershipStatus.Disabled;
    public void Enable() => Status = MembershipStatus.Active;

    public bool IsActive => Status == MembershipStatus.Active;
}

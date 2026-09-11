using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Users;

/// <summary>Tenant- or platform-scoped role assignment for a user.</summary>
public class UserRole : Entity, ITenantOwned
{
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public RoleType Role { get; private set; }

    private UserRole() { }

    public UserRole(Guid userId, RoleType role, Guid tenantId = default)
    {
        UserId = userId;
        Role = role;
        TenantId = tenantId;
    }
}

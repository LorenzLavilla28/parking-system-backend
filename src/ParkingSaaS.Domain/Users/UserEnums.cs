namespace ParkingSaaS.Domain.Users;

/// <summary>
/// System roles. <see cref="PlatformAdministrator"/> is the only role that is
/// not bound to a tenant and uses separate authorization policies.
/// </summary>
public enum RoleType
{
    PlatformAdministrator = 1,
    TenantAdministrator = 2,
    Supervisor = 3,
    Guard = 4
}

public enum UserStatus
{
    Active = 1,
    Disabled = 2,
    Locked = 3
}

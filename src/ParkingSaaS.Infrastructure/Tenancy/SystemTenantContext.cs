using ParkingSaaS.Application.Abstractions;

namespace ParkingSaaS.Infrastructure.Tenancy;

/// <summary>
/// Tenant context for non-HTTP execution (seeding, migrations, background jobs)
/// that needs to operate across all tenants. Behaves like a platform admin so
/// the persistence interceptor and query filters allow cross-tenant access.
/// </summary>
public sealed class SystemTenantContext : ITenantContext
{
    public Guid TenantId => Guid.Empty;
    public Guid? ParkingLocationId => null;
    public bool IsPlatformAdministrator => true;
    public bool HasTenant => false;
}

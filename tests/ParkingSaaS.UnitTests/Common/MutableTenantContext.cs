using ParkingSaaS.Application.Abstractions;

namespace ParkingSaaS.UnitTests.Common;

/// <summary>Test double for <see cref="ITenantContext"/> whose scope can be switched between assertions.</summary>
public sealed class MutableTenantContext : ITenantContext
{
    public Guid TenantId { get; set; }
    public Guid? ParkingLocationId { get; set; }
    public bool IsPlatformAdministrator { get; set; }
    public bool HasTenant => TenantId != Guid.Empty;

    public void ScopeTo(Guid tenantId)
    {
        TenantId = tenantId;
        IsPlatformAdministrator = false;
    }

    public void ScopeToPlatformAdmin()
    {
        TenantId = Guid.Empty;
        IsPlatformAdministrator = true;
    }
}

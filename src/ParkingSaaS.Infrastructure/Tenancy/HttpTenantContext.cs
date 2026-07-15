using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common;
using ParkingSaaS.Infrastructure.Identity;

namespace ParkingSaaS.Infrastructure.Tenancy;

/// <summary>
/// Resolves the tenant scope for the current request. For authenticated staff
/// it reads the JWT claims. Public customer requests resolve their tenant
/// through a separate mechanism (location slug / public token) handled in later
/// phases; here an unauthenticated request simply has no tenant.
/// </summary>
public sealed class HttpTenantContext : ITenantContext
{
    private readonly Guid _tenantId;
    private readonly Guid? _locationId;
    private readonly bool _isPlatformAdmin;

    public HttpTenantContext(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return;

        _isPlatformAdmin = user.IsInRole(RoleNames.PlatformAdministrator);

        var tenantClaim = user.FindFirstValue(AppClaimTypes.TenantId);
        if (Guid.TryParse(tenantClaim, out var tenantId))
            _tenantId = tenantId;

        var locationClaim = user.FindFirstValue(AppClaimTypes.LocationId);
        if (Guid.TryParse(locationClaim, out var locationId))
            _locationId = locationId;
    }

    public Guid TenantId => _tenantId;
    public Guid? ParkingLocationId => _locationId;
    public bool IsPlatformAdministrator => _isPlatformAdmin;
    public bool HasTenant => _tenantId != Guid.Empty;
}

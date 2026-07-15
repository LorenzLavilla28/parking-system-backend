using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common;
using ParkingSaaS.Infrastructure.Identity;

namespace ParkingSaaS.Api.Realtime;

/// <summary>
/// Realtime session hub. Clients subscribe to the location or tenant they are
/// currently viewing; the server authorizes each subscription from JWT claims
/// (a Hub method invocation has no reliable <c>HttpContext</c>, so we never lean
/// on the ambient tenant filter — every check is scoped explicitly by the token's
/// tenant using <c>IgnoreQueryFilters</c>).
/// </summary>
[Authorize(Policy = AuthorizationPolicies.GuardOrAbove)]
public sealed class SessionsHub : Hub
{
    private readonly IApplicationDbContext _db;

    public SessionsHub(IApplicationDbContext db) => _db = db;

    public async Task SubscribeToLocation(Guid locationId)
    {
        if (!TryGetTenant(out var tenantId))
            return;

        var isSupervisorOrAdmin = IsSupervisorOrAdmin();

        var locationInTenant = await _db.ParkingLocations
            .IgnoreQueryFilters()
            .AnyAsync(l => l.Id == locationId && l.TenantId == tenantId, Context.ConnectionAborted);

        var isAssignedGuard = false;
        if (!isSupervisorOrAdmin && Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            isAssignedGuard = await _db.UserParkingLocations
                .IgnoreQueryFilters()
                .AnyAsync(a => a.UserId == userId && a.ParkingLocationId == locationId && a.TenantId == tenantId,
                    Context.ConnectionAborted);
        }

        if (SessionHubAccess.CanJoinLocation(isSupervisorOrAdmin, locationInTenant, isAssignedGuard))
            await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroups.Location(locationId));
    }

    public Task UnsubscribeFromLocation(Guid locationId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, SessionGroups.Location(locationId));

    public async Task SubscribeToTenant()
    {
        if (!TryGetTenant(out var tenantId))
            return;

        if (SessionHubAccess.CanJoinTenant(IsSupervisorOrAdmin()))
            await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroups.Tenant(tenantId));
    }

    public Task UnsubscribeFromTenant()
    {
        return TryGetTenant(out var tenantId)
            ? Groups.RemoveFromGroupAsync(Context.ConnectionId, SessionGroups.Tenant(tenantId))
            : Task.CompletedTask;
    }

    private bool TryGetTenant(out Guid tenantId)
    {
        tenantId = Guid.Empty;
        return Guid.TryParse(Context.User?.FindFirstValue(AppClaimTypes.TenantId), out tenantId)
               && tenantId != Guid.Empty;
    }

    private bool IsSupervisorOrAdmin()
        => Context.User?.IsInRole(RoleNames.TenantAdministrator) == true
           || Context.User?.IsInRole(RoleNames.Supervisor) == true;
}

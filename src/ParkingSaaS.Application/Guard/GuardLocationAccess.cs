using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Application.Guard;

/// <summary>
/// Shared check that a staff member may operate at a given location. Supervisors
/// and tenant administrators may use any location in their tenant; plain guards
/// are restricted to the locations explicitly assigned to them.
/// </summary>
internal static class GuardLocationAccess
{
    public static async Task EnsureCanOperateAsync(
        IApplicationDbContext db, ICurrentUser user, Guid parkingLocationId, CancellationToken ct)
    {
        // The location must exist within the tenant (enforced by the global filter).
        var locationExists = await db.ParkingLocations.AnyAsync(l => l.Id == parkingLocationId, ct);
        if (!locationExists)
            throw new NotFoundException("Parking location not found.");

        var isSupervisorOrAdmin = user.Roles.Contains(RoleType.Supervisor) || user.Roles.Contains(RoleType.TenantAdministrator);
        if (isSupervisorOrAdmin)
            return;

        var assigned = await db.UserParkingLocations
            .AnyAsync(a => a.UserId == user.UserId && a.ParkingLocationId == parkingLocationId, ct);
        if (!assigned)
            throw new ForbiddenException("You are not assigned to this parking location.");
    }
}

using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Contracts.Guard;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Application.Guard;

/// <summary>
/// Lists the locations the current staff member may operate. All queries are
/// tenant-scoped by the global filter; guards are further restricted to their
/// explicit assignments.
/// </summary>
public sealed class GuardLocationService : IGuardLocationService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;

    public GuardLocationService(IApplicationDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<IReadOnlyList<GuardLocationResponse>> GetMyLocationsAsync(CancellationToken ct)
    {
        var query = _db.ParkingLocations.AsNoTracking().Where(l => l.Status == LocationStatus.Active);

        var isSupervisorOrAdmin =
            _user.Roles.Contains(RoleType.Supervisor) || _user.Roles.Contains(RoleType.TenantAdministrator);

        if (!isSupervisorOrAdmin)
        {
            var assignedIds = _db.UserParkingLocations
                .Where(a => a.UserId == _user.UserId)
                .Select(a => a.ParkingLocationId);
            query = query.Where(l => assignedIds.Contains(l.Id));
        }

        var locations = await query.OrderBy(l => l.Name).ToListAsync(ct);
        return locations
            .Select(l => new GuardLocationResponse(l.Id, l.Name, l.Slug, l.Timezone, l.AllowCashPayment, l.SlotCapacity))
            .ToArray();
    }
}

using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Domain.RatePlans;

namespace ParkingSaaS.Application.Pricing;

/// <summary>
/// Picks the rate plan version in effect for a location: among that location's
/// active plans, the newest version whose effective window contains the instant.
/// Queries bypass the tenant filter because this runs in both staff (tenant)
/// and public (no tenant) contexts; the location id already scopes the data.
/// </summary>
public sealed class ActiveRatePlanResolver : IActiveRatePlanResolver
{
    private readonly IApplicationDbContext _db;

    public ActiveRatePlanResolver(IApplicationDbContext db) => _db = db;

    public async Task<Guid?> ResolveActiveVersionIdAsync(Guid parkingLocationId, DateTimeOffset at, CancellationToken ct)
    {
        var assignedPlanId = await _db.ParkingLocations
            .IgnoreQueryFilters()
            .Where(l => l.Id == parkingLocationId)
            .Select(l => l.ActiveRatePlanId)
            .FirstOrDefaultAsync(ct);

        var activePlanIds = _db.RatePlans
            .IgnoreQueryFilters()
            .Where(p => p.Status == RatePlanStatus.Active &&
                        (assignedPlanId.HasValue
                            ? p.Id == assignedPlanId.Value
                            : p.ParkingLocationId == parkingLocationId))
            .Select(p => p.Id);

        var version = await _db.RatePlanVersions
            .IgnoreQueryFilters()
            .Where(v => activePlanIds.Contains(v.RatePlanId)
                        && v.EffectiveFrom <= at
                        && (v.EffectiveTo == null || v.EffectiveTo > at))
            .OrderByDescending(v => v.EffectiveFrom)
            .ThenByDescending(v => v.VersionNumber)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(ct);

        return version;
    }
}

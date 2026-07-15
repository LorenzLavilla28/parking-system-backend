using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Locations;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.RatePlans;

namespace ParkingSaaS.Application.Locations;

/// <summary>
/// Tenant-scoped CRUD for parking locations. Every query runs against the
/// EF global tenant filter, so callers can never reach another tenant's rows;
/// the <see cref="ITenantContext"/> only supplies the TenantId for new records.
/// </summary>
public sealed class LocationService : ILocationService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public LocationService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<LocationResponse> CreateAsync(CreateLocationRequest request, CancellationToken ct)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();
        var exists = await _db.ParkingLocations.AnyAsync(l => l.Slug == slug, ct);
        if (exists)
            throw new ConflictException($"A location with slug '{slug}' already exists.");

        var location = new ParkingLocation(_tenant.TenantId, request.Name, slug, request.Timezone, request.Address);
        location.UpdateDetails(request.Address, request.Timezone, request.ExitGraceMinutes, request.AllowCashPayment);

        await _db.ParkingLocations.AddAsync(location, ct);
        await _db.SaveChangesAsync(ct);
        return location.ToResponse();
    }

    public async Task<LocationResponse> UpdateAsync(Guid id, UpdateLocationRequest request, CancellationToken ct)
    {
        var location = await _db.ParkingLocations.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("Parking location not found.");

        location.Rename(request.Name);
        location.UpdateDetails(request.Address, request.Timezone, request.ExitGraceMinutes, request.AllowCashPayment);

        if (request.ClearRatePlan)
        {
            location.AssignRatePlan(null);
        }
        else if (request.RatePlanId.HasValue)
        {
            var planExists = await _db.RatePlans.AnyAsync(p =>
                p.Id == request.RatePlanId.Value &&
                p.Status == RatePlanStatus.Active, ct);
            if (!planExists)
                throw new ConflictException("The selected rate plan is not active.");
            location.AssignRatePlan(request.RatePlanId);
        }
        await _db.SaveChangesAsync(ct);
        return location.ToResponse();
    }

    public async Task<LocationResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var location = await _db.ParkingLocations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("Parking location not found.");
        return location.ToResponse();
    }

    public async Task<PagedResult<LocationResponse>> ListAsync(PageQuery query, CancellationToken ct)
    {
        var q = _db.ParkingLocations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            q = q.Where(l => l.Name.ToLower().Contains(term) || l.Slug.Contains(term));
        }

        q = query.Sort?.ToLowerInvariant() switch
        {
            "name" => q.OrderBy(l => l.Name),
            "-name" => q.OrderByDescending(l => l.Name),
            "-created" => q.OrderByDescending(l => l.CreatedAt),
            _ => q.OrderBy(l => l.CreatedAt)
        };

        var total = await q.LongCountAsync(ct);
        var items = await q
            .Skip((query.NormalizedPage - 1) * query.NormalizedPageSize)
            .Take(query.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<LocationResponse>(
            items.Select(l => l.ToResponse()).ToArray(),
            query.NormalizedPage,
            query.NormalizedPageSize,
            total);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var location = await _db.ParkingLocations.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("Parking location not found.");
        location.ChangeStatus(LocationStatus.Archived);
        await _db.SaveChangesAsync(ct);
    }
}

using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Locations;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.RatePlans;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Tenants;

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
        ParkingLocation location = null!;
        await _db.ExecuteInTransactionAsync(async txct =>
        {
            // Serialize included-location provisioning so two tenant-admin requests
            // cannot both consume the final location allowance.
            await _db.LockTenantAsync(_tenant.TenantId, txct);
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, txct)
                ?? throw new NotFoundException("Tenant not found.");
            var limits = SubscriptionPlanRules.For(tenant.SubscriptionPlan);
            var activeLocationCount = await _db.ParkingLocations.CountAsync(l => l.Status == LocationStatus.Active, txct);
            if (limits.MaximumLocations is null)
                throw new ConflictException("custom_plan_requires_platform_approval");
            if (activeLocationCount >= limits.MaximumLocations.Value)
                throw new ConflictException($"location_limit_reached: {tenant.SubscriptionPlan} includes up to {limits.MaximumLocations.Value} active location(s).");
            var effectiveMaximumSlots = SubscriptionPlanRules.EffectiveMaximumSlotsPerLocation(tenant.SubscriptionPlan, tenant.AdditionalSlotCapacity);
            if (effectiveMaximumSlots is { } maxSlots && request.SlotCapacity > maxSlots)
                throw new ConflictException($"capacity_not_allowed: {tenant.SubscriptionPlan} allows up to {maxSlots} slots per location including {tenant.AdditionalSlotCapacity} add-on slot(s).");

            var slug = request.Slug.Trim().ToLowerInvariant();
            var exists = await _db.ParkingLocations.AnyAsync(l => l.Slug == slug, txct);
            if (exists)
                throw new ConflictException($"A location with slug '{slug}' already exists.");

            location = new ParkingLocation(_tenant.TenantId, request.Name, slug, request.Timezone, request.Address, request.SlotCapacity);
            location.UpdateDetails(request.Address, request.Timezone, request.AllowCashPayment, request.SlotCapacity);

            await _db.ParkingLocations.AddAsync(location, txct);
            await _db.SaveChangesAsync(txct);
        }, ct);
        return location.ToResponse();
    }

    public async Task<LocationResponse> UpdateAsync(Guid id, UpdateLocationRequest request, CancellationToken ct)
    {
        var location = await _db.ParkingLocations.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("Parking location not found.");

        location.Rename(request.Name);
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct)
            ?? throw new NotFoundException("Tenant not found.");
        var effectiveMaximumSlots = SubscriptionPlanRules.EffectiveMaximumSlotsPerLocation(tenant.SubscriptionPlan, tenant.AdditionalSlotCapacity);
        if (effectiveMaximumSlots is { } maxSlots && request.SlotCapacity > maxSlots)
            throw new ConflictException($"capacity_not_allowed: {tenant.SubscriptionPlan} allows up to {maxSlots} slots per location including {tenant.AdditionalSlotCapacity} add-on slot(s).");
        var activeOccupancy = await _db.ParkingSessions.CountAsync(s =>
            s.ParkingLocationId == location.Id &&
            (s.Status == ParkingSessionStatus.ActiveUnpaid ||
             s.Status == ParkingSessionStatus.PaymentPending ||
             s.Status == ParkingSessionStatus.PaidExitWindow ||
             s.Status == ParkingSessionStatus.OverstayDue), ct);
        if (request.SlotCapacity < activeOccupancy)
            throw new ConflictException($"capacity_below_occupancy: this location currently has {activeOccupancy} active vehicle(s).");

        location.UpdateDetails(request.Address, request.Timezone, request.AllowCashPayment, request.SlotCapacity);

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

    public async Task<LocationQuotaResponse> GetQuotaAsync(CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct)
            ?? throw new NotFoundException("Tenant not found.");
        var limits = SubscriptionPlanRules.For(tenant.SubscriptionPlan);
        var activeLocations = await _db.ParkingLocations
            .CountAsync(l => l.Status == LocationStatus.Active, ct);
        var canCreateLocation = limits.MaximumLocations is { } maximumLocations
            && activeLocations < maximumLocations;

        return new LocationQuotaResponse(
            tenant.SubscriptionPlan.ToString(),
            activeLocations,
            limits.MaximumLocations,
            limits.MaximumSlotsPerLocation,
            canCreateLocation,
            tenant.AdditionalSlotCapacity,
            SubscriptionPlanRules.EffectiveMaximumSlotsPerLocation(tenant.SubscriptionPlan, tenant.AdditionalSlotCapacity));
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var location = await _db.ParkingLocations.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException("Parking location not found.");
        location.ChangeStatus(LocationStatus.Archived);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RestoreAsync(Guid id, CancellationToken ct)
    {
        await _db.ExecuteInTransactionAsync(async txct =>
        {
            await _db.LockTenantAsync(_tenant.TenantId, txct);
            var location = await _db.ParkingLocations.FirstOrDefaultAsync(l => l.Id == id, txct)
                ?? throw new NotFoundException("Parking location not found.");
            if (location.Status == LocationStatus.Active)
                return;

            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, txct)
                ?? throw new NotFoundException("Tenant not found.");
            var limits = SubscriptionPlanRules.For(tenant.SubscriptionPlan);
            var activeLocationCount = await _db.ParkingLocations.CountAsync(l => l.Status == LocationStatus.Active, txct);
            if (limits.MaximumLocations is null)
                throw new ConflictException("custom_plan_requires_platform_approval");
            if (activeLocationCount >= limits.MaximumLocations.Value)
                throw new ConflictException($"location_limit_reached: {tenant.SubscriptionPlan} includes up to {limits.MaximumLocations.Value} active location(s).");

            location.ChangeStatus(LocationStatus.Active);
            await _db.SaveChangesAsync(txct);
        }, ct);
    }
}

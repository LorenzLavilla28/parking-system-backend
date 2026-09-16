using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Audit;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Tenants;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Tenants;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Application.Tenants;

/// <summary>
/// Creates and lifecycle-manages tenants. Runs only under the platform-admin
/// policy, so it bypasses the tenant query filter and provisions the tenant's
/// first administrator atomically with the tenant record. Operational
/// locations are created later by the tenant administrator.
/// </summary>
public sealed class TenantProvisioningService : ITenantProvisioningService
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailQueue _emailQueue;
    private readonly IDateTime _clock;
    private readonly IAuditLogger? _audit;

    public TenantProvisioningService(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        IEmailQueue emailQueue,
        IDateTime clock,
        IAuditLogger? audit = null)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _emailQueue = emailQueue;
        _clock = clock;
        _audit = audit;
    }

    public async Task<TenantResponse> CreateAsync(CreateTenantRequest request, CancellationToken ct)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await _db.Tenants.AnyAsync(t => t.Slug == slug, ct))
            throw new ConflictException($"A tenant with slug '{slug}' already exists.");

        var adminEmail = request.AdminEmail.Trim().ToLowerInvariant();
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == adminEmail, ct))
            throw new ConflictException("A user with the administrator email already exists.");

        if (!Enum.TryParse<SubscriptionPlan>(request.SubscriptionPlan, ignoreCase: true, out var plan))
            throw new ConflictException($"Unknown subscription plan '{request.SubscriptionPlan}'.");
        if (plan == SubscriptionPlan.Free)
            throw new ConflictException("The Free plan is no longer available for new onboarding.");

        var limits = SubscriptionPlanRules.For(plan);
        var purchasedCapacity = request.PurchasedSlotCapacityPerLocation ?? limits.MaximumSlotsPerLocation;
        if (purchasedCapacity is { } selectedCapacity)
        {
            if (selectedCapacity < 1)
                throw new ConflictException("Purchased capacity must be at least 1 slot.");
            if (limits.MaximumSlotsPerLocation is { } maximumSlots && selectedCapacity > maximumSlots)
                throw new ConflictException($"{plan} supports up to {maximumSlots} slots per location before add-ons.");
        }

        var tenant = new Tenant(request.Name, slug, plan, request.DefaultCurrency, request.DefaultTimezone);
        tenant.SetPurchasedSlotCapacityPerLocation(purchasedCapacity);
        tenant.SetCapacityPricingEnabled(plan != SubscriptionPlan.Custom);
        await _db.Tenants.AddAsync(tenant, ct);

        var admin = new ApplicationUser(
            tenant.Id,
            request.AdminFirstName,
            request.AdminLastName,
            adminEmail,
            _passwordHasher.Hash(request.AdminPassword),
            mustChangePassword: true);
        admin.AddRole(RoleType.TenantAdministrator);
        await _db.Users.AddAsync(admin, ct);

        // Queue the onboarding email in the same transaction as the tenant/admin creation,
        // so it's never sent for a tenant that failed to persist (and never lost).
        _emailQueue.QueueTenantOnboarding(
            tenant.Id, adminEmail, $"{request.AdminFirstName} {request.AdminLastName}".Trim(),
            tenant.Name, tenant.Slug, request.AdminPassword, _clock.UtcNow);

        await _db.SaveChangesAsync(ct);
        return ToResponse(tenant, 0);
    }

    public async Task<TenantResponse> ChangeStatusAsync(Guid id, UpdateTenantStatusRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Tenant not found.");

        if (!Enum.TryParse<TenantStatus>(request.Status, ignoreCase: true, out var status))
            throw new ConflictException($"Unknown tenant status '{request.Status}'.");
        if (status == TenantStatus.Suspended && string.IsNullOrWhiteSpace(request.Reason))
            throw new ConflictException("A reason is required before suspending a tenant.");

        var activeLocationCount = await ActiveLocationCountAsync(id, ct);
        var previousStatus = tenant.Status.ToString();
        if (tenant.Status == status)
            return ToResponse(tenant, activeLocationCount);

        tenant.ChangeStatus(status);
        await AddAuditAsync(
            id,
            "tenant.status_changed",
            new { status = previousStatus },
            new
            {
                status = status.ToString(),
                effectiveDate = "Immediately",
                billingImpact = "No billing change",
            },
            request.Reason,
            ct);
        await _db.SaveChangesAsync(ct);
        return ToResponse(tenant, activeLocationCount);
    }

    public async Task<TenantResponse> ChangePlanAsync(Guid id, UpdateTenantPlanRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Tenant not found.");

        if (!Enum.TryParse<SubscriptionPlan>(request.SubscriptionPlan, ignoreCase: true, out var plan) || plan == SubscriptionPlan.Free)
            throw new ConflictException($"Unknown or unavailable subscription plan '{request.SubscriptionPlan}'.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ConflictException("A reason is required before changing a tenant's plan.");
        if (!string.Equals(request.EffectiveDate, "Immediately", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Next billing cycle plan changes are not supported yet. Select Immediately.");

        var limits = SubscriptionPlanRules.For(plan);
        var activeLocations = await _db.ParkingLocations
            .Where(l => l.TenantId == id && l.Status == LocationStatus.Active)
            .ToListAsync(ct);

        if (limits.MaximumLocations is { } maximumLocations && activeLocations.Count > maximumLocations)
            throw new ConflictException($"plan_downgrade_blocked: {plan} allows up to {maximumLocations} active location(s), but this tenant has {activeLocations.Count}.");

        if (limits.MaximumSlotsPerLocation is { } maximumSlots)
        {
            var effectiveMaximum = SubscriptionPlanRules.EffectiveMaximumSlotsPerLocation(
                plan,
                tenant.AdditionalSlotCapacity,
                tenant.PurchasedSlotCapacityPerLocation,
                tenant.CapacityPricingEnabled) ?? maximumSlots;
            var oversized = activeLocations.FirstOrDefault(l => l.SlotCapacity > effectiveMaximum);
            if (oversized is not null)
                throw new ConflictException($"plan_downgrade_blocked: {oversized.Name} uses {oversized.SlotCapacity} slots, above the new effective limit of {effectiveMaximum}.");
        }

        var previousPlan = tenant.SubscriptionPlan;
        if (previousPlan == plan)
            return ToResponse(tenant, activeLocations.Count);

        tenant.ChangePlan(plan);
        await AddAuditAsync(
            id,
            "tenant.plan_changed",
            new
            {
                subscriptionPlan = previousPlan.ToString(),
                monthlyPrice = SubscriptionPlanRules.MonthlyPrice(
                    previousPlan,
                    tenant.PurchasedSlotCapacityPerLocation,
                    tenant.AdditionalSlotCapacity,
                    tenant.CapacityPricingEnabled),
            },
            new
            {
                subscriptionPlan = plan.ToString(),
                monthlyPrice = SubscriptionPlanRules.MonthlyPrice(
                    plan,
                    tenant.PurchasedSlotCapacityPerLocation,
                    tenant.AdditionalSlotCapacity,
                    tenant.CapacityPricingEnabled),
                effectiveDate = "Immediately",
                proratedCharge = (decimal?)null,
                existingAddons = "No change",
                billingImpact = tenant.CapacityPricingEnabled
                    ? "Monthly price recalculated from purchased capacity; recurring collection is not configured in this workspace."
                    : "Fixed legacy plan pricing retained; recurring collection is not configured in this workspace.",
            },
            request.Reason,
            ct);
        await _db.SaveChangesAsync(ct);
        return ToResponse(tenant, activeLocations.Count);
    }

    /// <summary>
    /// Updates the platform-approved per-location capacity and synchronizes all
    /// existing tenant locations so the tenant dashboard and entry guard use the
    /// same capacity immediately.
    /// </summary>
    public async Task<TenantResponse> UpdateCapacityAddonAsync(Guid id, UpdateTenantCapacityAddonRequest request, CancellationToken ct)
    {
        if (request.AdditionalSlotCapacity < 0)
            throw new ConflictException("Additional capacity cannot be negative.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ConflictException("A reason is required before changing capacity.");

        TenantResponse? response = null;
        await _db.ExecuteInTransactionAsync(async txct =>
        {
            // Serialize the capacity change with location creation and other
            // tenant-level provisioning changes.
            await _db.LockTenantAsync(id, txct);

            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, txct)
                ?? throw new NotFoundException("Tenant not found.");

            var plan = tenant.SubscriptionPlan;
            var previousCapacity = tenant.AdditionalSlotCapacity;
            var tenantChanged = previousCapacity != request.AdditionalSlotCapacity;
            var previousPrice = SubscriptionPlanRules.MonthlyPrice(
                plan,
                tenant.PurchasedSlotCapacityPerLocation,
                previousCapacity,
                tenant.CapacityPricingEnabled);

            if (tenantChanged)
            {
                tenant.SetAdditionalSlotCapacity(request.AdditionalSlotCapacity);
                if (!tenant.CapacityPricingEnabled && SubscriptionPlanRules.For(plan).MaximumSlotsPerLocation is { } baseMaximum)
                {
                    tenant.SetPurchasedSlotCapacityPerLocation(baseMaximum);
                    tenant.SetCapacityPricingEnabled(true);
                }
            }

            var effectiveMaximum = SubscriptionPlanRules.EffectiveMaximumSlotsPerLocation(
                plan,
                tenant.AdditionalSlotCapacity,
                tenant.PurchasedSlotCapacityPerLocation,
                tenant.CapacityPricingEnabled);
            var locationChanges = await SynchronizeLocationCapacitiesAsync(id, effectiveMaximum, txct);

            if (!tenantChanged && locationChanges.Count == 0)
            {
                response = ToResponse(tenant, await ActiveLocationCountAsync(id, txct));
                return;
            }

            var newPrice = SubscriptionPlanRules.MonthlyPrice(
                plan,
                tenant.PurchasedSlotCapacityPerLocation,
                tenant.AdditionalSlotCapacity,
                tenant.CapacityPricingEnabled);
            await AddAuditAsync(
                id,
                "tenant.capacity_addon_changed",
                new
                {
                    additionalSlotCapacity = previousCapacity,
                    monthlyPrice = previousPrice,
                    locationCapacities = locationChanges
                        .Select(change => new
                        {
                            locationId = change.LocationId,
                            locationName = change.LocationName,
                            slotCapacity = change.PreviousCapacity,
                        })
                        .ToArray(),
                },
                new
                {
                    additionalSlotCapacity = request.AdditionalSlotCapacity,
                    purchasedSlotCapacityPerLocation = tenant.PurchasedSlotCapacityPerLocation,
                    monthlyPrice = newPrice,
                    effectiveCapacityPerLocation = effectiveMaximum,
                    synchronizedLocationCount = locationChanges.Count,
                    effectiveDate = "Immediately",
                    billingImpact = "Monthly price recalculated from capacity; recurring collection is not configured in this workspace.",
                },
                request.Reason,
                txct);
            await _db.SaveChangesAsync(txct);
            response = ToResponse(tenant, await ActiveLocationCountAsync(id, txct));
        }, ct);

        return response ?? throw new InvalidOperationException("Capacity update did not produce a tenant response.");
    }

    public async Task<TenantResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Tenant not found.");
        return ToResponse(tenant, await ActiveLocationCountAsync(id, ct));
    }

    public async Task<PagedResult<TenantResponse>> ListAsync(PageQuery query, CancellationToken ct)
    {
        var q = _db.Tenants.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            q = q.Where(t => t.Name.ToLower().Contains(term) || t.Slug.Contains(term));
        }

        q = query.Sort?.ToLowerInvariant() switch
        {
            "name" => q.OrderBy(t => t.Name),
            "-created" => q.OrderByDescending(t => t.CreatedAt),
            _ => q.OrderBy(t => t.CreatedAt)
        };

        var total = await q.LongCountAsync(ct);
        var items = await q
            .Skip((query.NormalizedPage - 1) * query.NormalizedPageSize)
            .Take(query.NormalizedPageSize)
            .ToListAsync(ct);

        var tenantIds = items.Select(t => t.Id).ToArray();
        var locationCounts = await _db.ParkingLocations
            .Where(l => tenantIds.Contains(l.TenantId) && l.Status == LocationStatus.Active)
            .GroupBy(l => l.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        return new PagedResult<TenantResponse>(
            items.Select(t => ToResponse(t, locationCounts.GetValueOrDefault(t.Id))).ToArray(),
            query.NormalizedPage,
            query.NormalizedPageSize,
            total);
    }

    public async Task<IReadOnlyList<TenantAuditLogResponse>> GetAuditHistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await _db.Tenants.AsNoTracking().AnyAsync(t => t.Id == id, ct))
            throw new NotFoundException("Tenant not found.");

        var logs = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.TenantId == id && a.EntityType == "Tenant")
            .OrderByDescending(a => a.CreatedAt)
            .Take(50)
            .ToListAsync(ct);
        var userIds = logs.Where(a => a.UserId.HasValue).Select(a => a.UserId!.Value).Distinct().ToArray();
        var users = await _db.Users.IgnoreQueryFilters()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new { Name = (u.FirstName + " " + u.LastName).Trim(), u.Email }, ct);

        return logs.Select(a =>
        {
            var administrator = a.UserId.HasValue && users.TryGetValue(a.UserId.Value, out var user)
                ? user.Name.Length > 0 ? user.Name : user.Email
                : "System";
            return new TenantAuditLogResponse(
                a.Id,
                a.Action,
                administrator,
                a.Reason,
                a.OldValuesJson,
                a.NewValuesJson,
                a.CreatedAt);
        }).ToArray();
    }

    private async Task<int> ActiveLocationCountAsync(Guid tenantId, CancellationToken ct)
        => await _db.ParkingLocations.CountAsync(l => l.TenantId == tenantId && l.Status == LocationStatus.Active, ct);

    private async Task<IReadOnlyList<LocationCapacityChange>> SynchronizeLocationCapacitiesAsync(
        Guid tenantId,
        int? effectiveMaximum,
        CancellationToken ct)
    {
        if (effectiveMaximum is not { } targetCapacity)
            return Array.Empty<LocationCapacityChange>();

        // Lock in a stable order so a capacity change cannot race vehicle entry
        // reservations at any affected location.
        var locationIds = await _db.ParkingLocations
            .Where(l => l.TenantId == tenantId)
            .OrderBy(l => l.Id)
            .Select(l => l.Id)
            .ToArrayAsync(ct);
        foreach (var locationId in locationIds)
            await _db.LockLocationAsync(locationId, ct);

        var locations = await _db.ParkingLocations
            .Where(l => l.TenantId == tenantId)
            .OrderBy(l => l.Id)
            .ToListAsync(ct);

        foreach (var location in locations)
        {
            var activeOccupancy = await _db.ParkingSessions.CountAsync(s =>
                s.ParkingLocationId == location.Id &&
                (s.Status == ParkingSessionStatus.ActiveUnpaid ||
                 s.Status == ParkingSessionStatus.PaymentPending ||
                 s.Status == ParkingSessionStatus.PaidExitWindow ||
                 s.Status == ParkingSessionStatus.OverstayDue), ct);
            if (activeOccupancy > targetCapacity)
                throw new ConflictException($"capacity_below_occupancy: {location.Name} currently has {activeOccupancy} active vehicle(s), but the requested capacity is {targetCapacity} slots.");
        }

        var changes = new List<LocationCapacityChange>();
        foreach (var location in locations)
        {
            if (location.SlotCapacity == targetCapacity)
                continue;

            changes.Add(new LocationCapacityChange(
                location.Id,
                location.Name,
                location.SlotCapacity,
                targetCapacity));
            location.SetSlotCapacity(targetCapacity);
        }

        return changes;
    }

    private sealed record LocationCapacityChange(
        Guid LocationId,
        string LocationName,
        int PreviousCapacity,
        int NewCapacity);

    private async Task AddAuditAsync(
        Guid tenantId,
        string action,
        object oldValues,
        object newValues,
        string? reason,
        CancellationToken ct)
    {
        if (_audit is null)
            return;

        await _audit.AddAsync(
            tenantId,
            null,
            action,
            "Tenant",
            tenantId.ToString(),
            oldValues,
            newValues,
            reason,
            new AuditContext(null, null),
            ct);
    }

    private static TenantResponse ToResponse(Tenant t, int activeLocationCount) => new(
        t.Id,
        t.Name,
        t.Slug,
        t.Status.ToString(),
        t.SubscriptionPlan.ToString(),
        t.DefaultCurrency,
        t.DefaultTimezone,
        SubscriptionPlanRules.For(t.SubscriptionPlan).MaximumLocations,
        SubscriptionPlanRules.For(t.SubscriptionPlan).MaximumSlotsPerLocation,
        SubscriptionPlanRules.MonthlyPrice(
            t.SubscriptionPlan,
            t.PurchasedSlotCapacityPerLocation,
            t.AdditionalSlotCapacity,
            t.CapacityPricingEnabled),
        SubscriptionPlanRules.PricePerSlot(t.SubscriptionPlan),
        t.PurchasedSlotCapacityPerLocation,
        t.CapacityPricingEnabled,
        t.AdditionalSlotCapacity,
        SubscriptionPlanRules.EffectiveMaximumSlotsPerLocation(
            t.SubscriptionPlan,
            t.AdditionalSlotCapacity,
            t.PurchasedSlotCapacityPerLocation,
            t.CapacityPricingEnabled),
        activeLocationCount,
        t.CreatedAt,
        t.UpdatedAt);
}

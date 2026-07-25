using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Tenants;
using ParkingSaaS.Domain.Locations;
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

    public TenantProvisioningService(IApplicationDbContext db, IPasswordHasher passwordHasher, IEmailQueue emailQueue, IDateTime clock)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _emailQueue = emailQueue;
        _clock = clock;
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

        var tenant = new Tenant(request.Name, slug, plan, request.DefaultCurrency, request.DefaultTimezone);
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
        return ToResponse(tenant);
    }

    public async Task<TenantResponse> CreateAddOnLocationAsync(Guid tenantId, CreateTenantAddOnLocationRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new NotFoundException("Tenant not found.");

        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await _db.ParkingLocations.IgnoreQueryFilters().AnyAsync(l => l.Slug == slug, ct))
            throw new ConflictException($"A location with slug '{slug}' already exists.");

        var monthlyPrice = SubscriptionPlanRules.AddOnPriceFor(request.SlotCapacity) ?? request.MonthlyPrice;
        if (monthlyPrice is null or <= 0m)
            throw new ConflictException("A location above 90 slots requires a custom monthly price and approval.");

        var location = new ParkingLocation(
            tenant.Id, request.Name, slug, request.Timezone, request.Address,
            request.SlotCapacity, isAddOn: true, monthlyPrice: monthlyPrice);
        location.UpdateDetails(request.Address, request.Timezone, request.AllowCashPayment, request.SlotCapacity);
        await _db.ParkingLocations.AddAsync(location, ct);
        await _db.SaveChangesAsync(ct);
        return ToResponse(tenant);
    }

    public async Task<TenantResponse> ChangeStatusAsync(Guid id, UpdateTenantStatusRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Tenant not found.");

        if (!Enum.TryParse<TenantStatus>(request.Status, ignoreCase: true, out var status))
            throw new ConflictException($"Unknown tenant status '{request.Status}'.");

        tenant.ChangeStatus(status);
        await _db.SaveChangesAsync(ct);
        return ToResponse(tenant);
    }

    public async Task<TenantResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Tenant not found.");
        return ToResponse(tenant);
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

        return new PagedResult<TenantResponse>(
            items.Select(t => ToResponse(t)).ToArray(),
            query.NormalizedPage,
            query.NormalizedPageSize,
            total);
    }

    private static TenantResponse ToResponse(Tenant t) => new(
        t.Id,
        t.Name,
        t.Slug,
        t.Status.ToString(),
        t.SubscriptionPlan.ToString(),
        t.DefaultCurrency,
        t.DefaultTimezone,
        SubscriptionPlanRules.For(t.SubscriptionPlan).MaximumLocations,
        SubscriptionPlanRules.For(t.SubscriptionPlan).MaximumSlotsPerLocation,
        SubscriptionPlanRules.For(t.SubscriptionPlan).MonthlyPrice,
        t.CreatedAt,
        t.UpdatedAt);
}

using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.RatePlans;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.RatePlans;

namespace ParkingSaaS.Application.RatePlans;

/// <summary>
/// Tenant-admin management of rate plans. Creating a plan also writes its first
/// version and activates it. Adding a version appends an immutable snapshot and
/// closes the prior version's effective window — existing sessions, which pin a
/// specific version, are unaffected.
/// </summary>
public sealed class RatePlanService : IRatePlanService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTime _clock;

    public RatePlanService(IApplicationDbContext db, ICurrentUser user, IDateTime clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public async Task<RatePlanResponse> CreateAsync(CreateRatePlanRequest request, CancellationToken ct)
    {
        var rulesJson = ValidateAndNormalizeRules(request.RulesJson);

        var plan = new RatePlan(_user.TenantId, request.Name, request.Description);
        plan.Activate();
        await _db.RatePlans.AddAsync(plan, ct);

        var version = new RatePlanVersion(_user.TenantId, plan.Id, 1, _clock.UtcNow, rulesJson, _user.UserId ?? Guid.Empty);
        await _db.RatePlanVersions.AddAsync(version, ct);

        await _db.SaveChangesAsync(ct);
        return ToResponse(plan, version.VersionNumber, PricingRules.Parse(rulesJson).PaidExitGraceMinutes, rulesJson);
    }

    public async Task<RatePlanVersionResponse> AddVersionAsync(Guid ratePlanId, AddRatePlanVersionRequest request, CancellationToken ct)
    {
        var plan = await _db.RatePlans.FirstOrDefaultAsync(p => p.Id == ratePlanId, ct)
            ?? throw new NotFoundException("Rate plan not found.");

        var rulesJson = ValidateAndNormalizeRules(request.RulesJson);
        var now = _clock.UtcNow;

        var versions = await _db.RatePlanVersions
            .Where(v => v.RatePlanId == plan.Id)
            .ToListAsync(ct);

        var nextNumber = versions.Count == 0 ? 1 : versions.Max(v => v.VersionNumber) + 1;

        // Close any currently-open version.
        foreach (var open in versions.Where(v => v.EffectiveTo == null))
            open.Supersede(now);

        var version = new RatePlanVersion(plan.TenantId, plan.Id, nextNumber, now, rulesJson, _user.UserId ?? Guid.Empty);
        await _db.RatePlanVersions.AddAsync(version, ct);

        if (plan.Status != RatePlanStatus.Active)
            plan.Activate();

        await _db.SaveChangesAsync(ct);
        return ToVersionResponse(version);
    }

    public async Task<RatePlanResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var plan = await _db.RatePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Rate plan not found.");
        var current = await CurrentVersionAsync(plan.Id, ct);
        return ToResponse(plan, current.VersionNumber, current.PaidExitGraceMinutes, current.RulesJson);
    }

    public async Task<PagedResult<RatePlanResponse>> ListAsync(Guid? parkingLocationId, PageQuery page, CancellationToken ct)
    {
        var q = _db.RatePlans.AsNoTracking().AsQueryable();
        if (parkingLocationId.HasValue)
            q = q.Where(p => p.ParkingLocationId == parkingLocationId.Value);

        q = q.OrderByDescending(p => p.CreatedAt);

        var total = await q.LongCountAsync(ct);
        var plans = await q
            .Skip((page.NormalizedPage - 1) * page.NormalizedPageSize)
            .Take(page.NormalizedPageSize)
            .ToListAsync(ct);

        var planIds = plans.Select(p => p.Id).ToList();
        var currentVersions = await _db.RatePlanVersions
            .Where(v => planIds.Contains(v.RatePlanId) && v.EffectiveTo == null)
            .Select(v => new { v.RatePlanId, v.VersionNumber, v.RulesJson })
            .ToListAsync(ct);
        var lookup = currentVersions.ToDictionary(
            x => x.RatePlanId,
            x => (VersionNumber: (int?)x.VersionNumber, PaidExitGraceMinutes: (int?)PricingRules.Parse(x.RulesJson).PaidExitGraceMinutes, RulesJson: x.RulesJson));

        var items = plans.Select(p =>
        {
            var current = lookup.GetValueOrDefault(p.Id);
            return ToResponse(p, current.VersionNumber, current.PaidExitGraceMinutes, current.RulesJson);
        }).ToArray();
        return new PagedResult<RatePlanResponse>(items, page.NormalizedPage, page.NormalizedPageSize, total);
    }

    public async Task<IReadOnlyList<RatePlanVersionResponse>> ListVersionsAsync(Guid ratePlanId, CancellationToken ct)
    {
        var exists = await _db.RatePlans.AnyAsync(p => p.Id == ratePlanId, ct);
        if (!exists) throw new NotFoundException("Rate plan not found.");

        var versions = await _db.RatePlanVersions
            .AsNoTracking()
            .Where(v => v.RatePlanId == ratePlanId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);

        return versions.Select(ToVersionResponse).ToArray();
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var plan = await _db.RatePlans.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Rate plan not found.");
        plan.Archive();
        var assignedLocations = await _db.ParkingLocations
            .Where(l => l.ActiveRatePlanId == plan.Id)
            .ToListAsync(ct);
        foreach (var location in assignedLocations) location.AssignRatePlan(null);
        await _db.SaveChangesAsync(ct);
    }

    private static string ValidateAndNormalizeRules(string rulesJson)
    {
        PricingRules rules;
        try
        {
            rules = PricingRules.Parse(rulesJson);
        }
        catch (Exception ex)
        {
            throw new ConflictException($"Invalid rules JSON: {ex.Message}");
        }

        var errors = rules.Validate();
        if (errors.Count > 0)
            throw new ConflictException($"Invalid pricing rules: {string.Join("; ", errors)}");

        // Re-serialize to a canonical form.
        return rules.Serialize();
    }

    private async Task<(int? VersionNumber, int? PaidExitGraceMinutes, string? RulesJson)> CurrentVersionAsync(Guid planId, CancellationToken ct)
    {
        var current = await _db.RatePlanVersions
            .Where(v => v.RatePlanId == planId && v.EffectiveTo == null)
            .Select(v => new { v.VersionNumber, v.RulesJson })
            .FirstOrDefaultAsync(ct);
        return current is null
            ? (null, null, null)
            : ((int?)current.VersionNumber, PricingRules.Parse(current.RulesJson).PaidExitGraceMinutes, current.RulesJson);
    }

    private static RatePlanResponse ToResponse(RatePlan p, int? currentVersion, int? paidExitGraceMinutes, string? rulesJson) => new(
        p.Id, p.ParkingLocationId, p.Name, p.Description, p.Status.ToString(), currentVersion, paidExitGraceMinutes, p.CreatedAt, p.UpdatedAt, rulesJson);

    private static RatePlanVersionResponse ToVersionResponse(RatePlanVersion v) => new(
        v.Id, v.RatePlanId, v.VersionNumber, v.EffectiveFrom, v.EffectiveTo, v.RulesJson, v.CreatedAt);
}

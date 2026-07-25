using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Application.Pricing;

/// <summary>
/// Loads the session's pinned rate plan version and the location's timezone,
/// then delegates the math to the pure <see cref="IParkingFeeCalculator"/>.
/// Bypasses tenant filters (it serves public and staff callers alike); access
/// is already gated by the caller resolving the session.
/// </summary>
public sealed class SessionPricingService : ISessionPricingService
{
    private readonly IApplicationDbContext _db;
    private readonly IParkingFeeCalculator _calculator;

    public SessionPricingService(IApplicationDbContext db, IParkingFeeCalculator calculator)
    {
        _db = db;
        _calculator = calculator;
    }

    public async Task<FeeCalculationResult?> CalculateAsync(
        ParkingSession session, DateTimeOffset at, DiscountInput? discount, CancellationToken ct)
    {
        if (session.RatePlanVersionId is not { } versionId)
            return null;

        var version = await _db.RatePlanVersions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
            return null;

        var timezone = await _db.ParkingLocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l => l.Id == session.ParkingLocationId)
            .Select(l => l.Timezone)
            .FirstOrDefaultAsync(ct) ?? "UTC";

        var rules = PricingRules.Parse(version.RulesJson);
        var input = new FeeCalculationInput(
            session.EntryTime, at, session.VehicleType, version.Id, version.VersionNumber, rules, timezone, discount);

        return _calculator.Calculate(input);
    }

    public async Task<int> GetPaidExitGraceMinutesAsync(ParkingSession session, CancellationToken ct)
    {
        if (session.RatePlanVersionId is not { } versionId)
            return 0;

        var rulesJson = await _db.RatePlanVersions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.Id == versionId)
            .Select(v => v.RulesJson)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(rulesJson))
            return 0;

        return PricingRules.Parse(rulesJson).PaidExitGraceMinutes;
    }
}

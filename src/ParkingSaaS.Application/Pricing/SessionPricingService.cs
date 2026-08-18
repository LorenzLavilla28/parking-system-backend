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
    private readonly IActiveRatePlanResolver _activeRatePlanResolver;

    public SessionPricingService(IApplicationDbContext db, IParkingFeeCalculator calculator)
        : this(db, calculator, new ActiveRatePlanResolver(db))
    {
    }

    public SessionPricingService(IApplicationDbContext db, IParkingFeeCalculator calculator, IActiveRatePlanResolver activeRatePlanResolver)
    {
        _db = db;
        _calculator = calculator;
        _activeRatePlanResolver = activeRatePlanResolver;
    }

    public async Task<FeeCalculationResult?> CalculateAsync(
        ParkingSession session, DateTimeOffset at, DiscountInput? discount, CancellationToken ct)
    {
        var version = session.RatePlanVersionId is { } pinnedVersionId
            ? await _db.RatePlanVersions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == pinnedVersionId, ct)
            : null;
        if (version is null)
        {
            // Legacy sessions created before rate-plan versions were pinned can
            // still be priced using the plan currently in effect at the location.
            var fallbackVersionId = await _activeRatePlanResolver.ResolveActiveVersionIdAsync(session.ParkingLocationId, at, ct);
            if (fallbackVersionId is not { } resolvedVersionId)
                return null;
            version = await _db.RatePlanVersions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == resolvedVersionId, ct);
        }
        if (version is null)
            return null;

        var timezone = await _db.ParkingLocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l => l.Id == session.ParkingLocationId)
            .Select(l => l.Timezone)
            .FirstOrDefaultAsync(ct) ?? "UTC";

        var rules = PricingRules.Parse(version.RulesJson);
        var calculationTime = at;
        if (session.PaidExitDeadline is { } deadline && at <= deadline)
        {
            // Paid exit grace pauses succeeding-hour billing until the deadline.
            // The stored deadline is anchored to the paid-through period, so
            // subtracting grace gives the point at which billing may resume.
            calculationTime = new[] { at, deadline.AddMinutes(-rules.PaidExitGraceMinutes) }.Min();
        }
        var input = new FeeCalculationInput(
            session.EntryTime, calculationTime, session.VehicleType, version.Id, version.VersionNumber, rules, timezone, discount);

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

    public async Task<DateTimeOffset> GetPaidExitDeadlineAsync(
        ParkingSession session, DateTimeOffset paidAt, CancellationToken ct)
    {
        var graceMinutes = await GetPaidExitGraceMinutesAsync(session, ct);
        var paidThrough = paidAt;

        if (session.RatePlanVersionId is { } versionId)
        {
            var version = await _db.RatePlanVersions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == versionId, ct);
            if (version is not null)
            {
                var timezone = await _db.ParkingLocations
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(l => l.Id == session.ParkingLocationId)
                    .Select(l => l.Timezone)
                    .FirstOrDefaultAsync(ct) ?? "UTC";
                var rules = PricingRules.Parse(version.RulesJson);
                var block = SelectBlock(rules, session.VehicleType, session.EntryTime, timezone);

                if (block.Type == RateType.FirstBlock && block.FirstHours > 0)
                {
                    var firstBlockEnd = session.EntryTime.AddHours(block.FirstHours);
                    if (paidAt <= firstBlockEnd)
                        paidThrough = firstBlockEnd;
                }
            }
        }

        return paidThrough.AddMinutes(graceMinutes);
    }

    private static RateBlock SelectBlock(
        PricingRules rules, VehicleType vehicleType, DateTimeOffset entryTime, string timezone)
    {
        var localEntry = ToLocal(entryTime, timezone);
        var isWeekend = localEntry.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var isHoliday = rules.Holidays.Contains(localEntry.ToString("yyyy-MM-dd"));

        if (isHoliday && rules.Holiday is not null) return rules.Holiday;
        if (isWeekend && rules.Weekend is not null) return rules.Weekend;
        if (rules.VehicleRates.TryGetValue(vehicleType.ToString(), out var vehicleBlock)) return vehicleBlock;
        return rules.Default;
    }

    private static DateTime ToLocal(DateTimeOffset value, string timezone)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return TimeZoneInfo.ConvertTime(value, tz).DateTime;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return value.UtcDateTime;
        }
    }
}

using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Domain.Pricing;

/// <summary>Identifies the rate block and calendar context that apply at entry.</summary>
public sealed record SelectedRateBlock(
    RateBlock Block,
    string Label,
    bool IsWeekend,
    bool IsHoliday);

/// <summary>
/// Shared rate-plan selection so fee calculation and printed rate summaries
/// always use the same vehicle, weekend, holiday, and timezone precedence.
/// </summary>
public static class PricingRuleSelector
{
    public static SelectedRateBlock SelectBlock(
        PricingRules rules,
        VehicleType vehicleType,
        DateTimeOffset entryTime,
        string timezone)
    {
        var localEntry = ToLocal(entryTime, timezone);
        var isWeekend = localEntry.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var isHoliday = rules.Holidays.Contains(localEntry.ToString("yyyy-MM-dd"));

        if (isHoliday && rules.Holiday is not null)
            return new SelectedRateBlock(rules.Holiday, "Holiday rate", isWeekend, true);
        if (isWeekend && rules.Weekend is not null)
            return new SelectedRateBlock(rules.Weekend, "Weekend rate", true, isHoliday);
        if (rules.VehicleRates.TryGetValue(vehicleType.ToString(), out var vehicleBlock))
            return new SelectedRateBlock(vehicleBlock, $"{vehicleType} rate", isWeekend, isHoliday);

        return new SelectedRateBlock(rules.Default, "Parking fee", isWeekend, isHoliday);
    }

    public static decimal Multiplier(PricingRules rules, SelectedRateBlock selection)
    {
        if (selection.IsHoliday && rules.HolidayMultiplier is { } holidayMultiplier)
            return holidayMultiplier;
        if (selection.IsWeekend && rules.WeekendMultiplier is { } weekendMultiplier)
            return weekendMultiplier;
        return 1m;
    }

    public static DateTime ToLocal(DateTimeOffset value, string timezone)
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

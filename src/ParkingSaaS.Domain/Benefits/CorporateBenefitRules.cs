using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Domain.Benefits;

public sealed class CorporateBenefitTimeWindow
{
    public string Start { get; set; } = "08:00";
    public string End { get; set; } = "20:00";
}

/// <summary>
/// Versioned, tenant-configurable rules. Time windows are interpreted in the
/// assigned parking location's timezone and may cross midnight.
/// </summary>
public sealed class CorporateBenefitRules
{
    public List<CorporateBenefitTimeWindow> Windows { get; set; } =
        new() { new CorporateBenefitTimeWindow() };
    public List<string> DaysOfWeek { get; set; } =
        new() { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" };
    public List<string> Holidays { get; set; } = new();
    public bool ExcludeHolidays { get; set; }
    public List<string> EligibleVehicleTypes { get; set; } = new();
    public string OutsideWindowBehavior { get; set; } = "NormalRate";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static CorporateBenefitRules Parse(string rulesJson)
    {
        if (string.IsNullOrWhiteSpace(rulesJson))
            throw new ArgumentException("Benefit rules are empty.", nameof(rulesJson));
        return JsonSerializer.Deserialize<CorporateBenefitRules>(rulesJson, JsonOptions)
            ?? throw new ArgumentException("Benefit rules could not be parsed.", nameof(rulesJson));
    }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Windows is null || Windows.Count == 0 || Windows.Count > 8)
            errors.Add("At least one and at most eight time windows are required.");
        else
        {
            foreach (var window in Windows)
            {
                if (!TryParseTime(window.Start, out _) || !TryParseTime(window.End, out _))
                    errors.Add("Benefit window times must use HH:mm format.");
                else if (window.Start == window.End)
                    errors.Add("A benefit window cannot have the same start and end time.");
            }
        }

        var validDays = new HashSet<string>(Enum.GetNames<DayOfWeek>(), StringComparer.OrdinalIgnoreCase);
        if (DaysOfWeek is null || DaysOfWeek.Count == 0 || DaysOfWeek.Any(day => !validDays.Contains(day)))
            errors.Add("At least one valid day of week is required.");

        if (Holidays is null || Holidays.Any(value => !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            errors.Add("Holidays must use yyyy-MM-dd format.");

        if (!string.Equals(OutsideWindowBehavior, "NormalRate", StringComparison.OrdinalIgnoreCase))
            errors.Add("Outside-window behavior must be NormalRate.");

        if (EligibleVehicleTypes is not null)
        {
            foreach (var type in EligibleVehicleTypes)
            {
                if (!Enum.TryParse<VehicleType>(type, true, out _))
                    errors.Add($"Unknown eligible vehicle type '{type}'.");
            }
        }

        return errors;
    }

    public bool IsVehicleEligible(VehicleType type)
        => EligibleVehicleTypes is null || EligibleVehicleTypes.Count == 0 ||
           EligibleVehicleTypes.Any(value => string.Equals(value, type.ToString(), StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<FreeTimeInterval> GetFreeIntervals(
        DateTimeOffset from, DateTimeOffset until, string timezone)
    {
        if (until <= from || Validate().Count > 0) return Array.Empty<FreeTimeInterval>();

        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch (TimeZoneNotFoundException) { tz = TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { tz = TimeZoneInfo.Utc; }

        var localFrom = TimeZoneInfo.ConvertTime(from, tz).DateTime.Date.AddDays(-1);
        var localUntil = TimeZoneInfo.ConvertTime(until, tz).DateTime.Date.AddDays(1);
        var result = new List<FreeTimeInterval>();

        for (var date = localFrom; date <= localUntil; date = date.AddDays(1))
        {
            if (!IsApplicableDate(date)) continue;

            foreach (var window in Windows)
            {
                var start = TimeOnly.ParseExact(window.Start, "HH:mm", CultureInfo.InvariantCulture);
                var end = TimeOnly.ParseExact(window.End, "HH:mm", CultureInfo.InvariantCulture);
                var localStart = date.Add(start.ToTimeSpan());
                var localEnd = date.Add(end.ToTimeSpan());
                if (end <= start) localEnd = localEnd.AddDays(1);

                var utcStart = ToUtc(localStart, tz);
                var utcEnd = ToUtc(localEnd, tz);
                if (utcStart is null || utcEnd is null || utcEnd <= utcStart) continue;

                var intervalStart = utcStart.Value < from ? from : utcStart.Value;
                var intervalEnd = utcEnd.Value > until ? until : utcEnd.Value;
                if (intervalEnd > intervalStart)
                    result.Add(new FreeTimeInterval(intervalStart, intervalEnd));
            }
        }

        return result.OrderBy(interval => interval.From).ToArray();
    }

    private bool IsApplicableDate(DateTime date)
    {
        if (!DaysOfWeek.Any(day => string.Equals(day, date.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase)))
            return false;
        if (!ExcludeHolidays) return true;
        return !Holidays.Contains(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryParseTime(string value, out TimeOnly time)
        => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static DateTimeOffset? ToUtc(DateTime local, TimeZoneInfo timezone)
    {
        if (timezone.IsInvalidTime(local)) return null;
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), timezone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}

public sealed record FreeTimeInterval(DateTimeOffset From, DateTimeOffset To);

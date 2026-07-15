using System.Text.Json;
using System.Text.Json.Serialization;

namespace ParkingSaaS.Domain.Pricing;

public enum RateType
{
    /// <summary>A single amount regardless of duration.</summary>
    Flat = 1,
    /// <summary>A first block (e.g. first 3 hours) then a succeeding increment.</summary>
    FirstBlock = 2,
    /// <summary>A repeating per-unit charge across the whole stay.</summary>
    PerUnit = 3
}

public enum IncrementUnit
{
    Hour = 1,
    Minute = 2,
    /// <summary>A configurable fraction of an hour (see <see cref="RateBlock.FractionMinutes"/>).</summary>
    Fraction = 3
}

public enum DiscountType
{
    None = 0,
    SeniorPwd = 1,
    Merchant = 2,
    Complimentary = 3
}

/// <summary>Describes how the time-based charge is computed.</summary>
public sealed class RateBlock
{
    public RateType Type { get; set; } = RateType.FirstBlock;

    // Flat
    public decimal FlatAmount { get; set; }

    // FirstBlock
    public int FirstHours { get; set; }
    public decimal FirstAmount { get; set; }
    public decimal IncrementAmount { get; set; }
    public IncrementUnit IncrementUnit { get; set; } = IncrementUnit.Hour;

    // PerUnit
    public decimal PerUnitAmount { get; set; }
    public IncrementUnit PerUnit { get; set; } = IncrementUnit.Hour;

    /// <summary>Size of a fraction in minutes, used when a unit is <see cref="IncrementUnit.Fraction"/>.</summary>
    public int FractionMinutes { get; set; } = 60;
}

/// <summary>Optional flat surcharge applied when a stay overlaps the overnight window.</summary>
public sealed class OvernightRule
{
    public decimal Fee { get; set; }
    public int StartHour { get; set; } = 22;
    public int EndHour { get; set; } = 6;
}

/// <summary>
/// The full, versioned pricing configuration stored as <c>RulesJson</c> on a
/// <c>RatePlanVersion</c>. Deserialized by the fee calculator; never hard-coded.
/// </summary>
public sealed class PricingRules
{
    public string Currency { get; set; } = "PHP";

    /// <summary>Free if the stay is within this many minutes of entry.</summary>
    public int EntryGraceMinutes { get; set; }

    /// <summary>Optional per-plan override of the location's paid-exit grace.</summary>
    public int? PaidExitGraceMinutes { get; set; }

    public RateBlock Default { get; set; } = new();

    /// <summary>Per-vehicle-type overrides, keyed by vehicle type name (e.g. "Motorcycle").</summary>
    public Dictionary<string, RateBlock> VehicleRates { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public RateBlock? Weekend { get; set; }
    public decimal? WeekendMultiplier { get; set; }

    public RateBlock? Holiday { get; set; }
    public decimal? HolidayMultiplier { get; set; }

    /// <summary>Local calendar dates (yyyy-MM-dd) treated as holidays.</summary>
    public List<string> Holidays { get; set; } = new();

    public decimal? DailyMax { get; set; }
    public OvernightRule? Overnight { get; set; }
    public decimal? LostTicketFee { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static PricingRules Parse(string rulesJson)
    {
        if (string.IsNullOrWhiteSpace(rulesJson))
            throw new ArgumentException("Rules JSON is empty.", nameof(rulesJson));
        var rules = JsonSerializer.Deserialize<PricingRules>(rulesJson, JsonOptions)
            ?? throw new ArgumentException("Rules JSON could not be parsed.", nameof(rulesJson));
        return rules;
    }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Validates the rules are internally consistent; returns error messages (empty when valid).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Currency) || Currency.Length != 3)
            errors.Add("Currency must be a 3-letter ISO code.");
        if (EntryGraceMinutes < 0) errors.Add("EntryGraceMinutes cannot be negative.");
        if (DailyMax is < 0) errors.Add("DailyMax cannot be negative.");
        if (LostTicketFee is < 0) errors.Add("LostTicketFee cannot be negative.");

        ValidateBlock(Default, "Default", errors);
        foreach (var (key, block) in VehicleRates) ValidateBlock(block, $"VehicleRates[{key}]", errors);
        if (Weekend is not null) ValidateBlock(Weekend, "Weekend", errors);
        if (Holiday is not null) ValidateBlock(Holiday, "Holiday", errors);

        if (Overnight is not null && Overnight.Fee < 0) errors.Add("Overnight.Fee cannot be negative.");
        return errors;
    }

    private static void ValidateBlock(RateBlock b, string name, List<string> errors)
    {
        if (b.FlatAmount < 0 || b.FirstAmount < 0 || b.IncrementAmount < 0 || b.PerUnitAmount < 0)
            errors.Add($"{name}: amounts cannot be negative.");
        if (b.FractionMinutes <= 0)
            errors.Add($"{name}: FractionMinutes must be positive.");
        if (b.Type == RateType.FirstBlock && b.FirstHours < 0)
            errors.Add($"{name}: FirstHours cannot be negative.");
    }
}

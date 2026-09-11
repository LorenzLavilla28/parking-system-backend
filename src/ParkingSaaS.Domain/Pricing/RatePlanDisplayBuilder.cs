using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Domain.Pricing;

/// <summary>
/// Builds the configured rate schedule for an entry ticket. This is a rate
/// summary, not a stay-specific fee calculation, so it does not include
/// time-dependent surcharges or discounts that may apply later.
/// </summary>
public static class RatePlanDisplayBuilder
{
    public static IReadOnlyList<PricingLineItem> Build(
        PricingRules rules,
        VehicleType vehicleType,
        DateTimeOffset entryTime,
        string timezone)
    {
        var selection = PricingRuleSelector.SelectBlock(rules, vehicleType, entryTime, timezone);
        var multiplier = PricingRuleSelector.Multiplier(rules, selection);
        var block = selection.Block;

        return block.Type switch
        {
            RateType.Flat => new[]
            {
                Line("flat", "Parking rate", block.FlatAmount * multiplier),
            },
            RateType.PerUnit => new[]
            {
                Line("per_unit", PerUnitDescription(block), block.PerUnitAmount * multiplier),
            },
            _ => FirstBlockLines(block, multiplier),
        };
    }

    private static IReadOnlyList<PricingLineItem> FirstBlockLines(RateBlock block, decimal multiplier)
    {
        var lines = new List<PricingLineItem>
        {
            Line("first_block", $"First {block.FirstHours} hours or part thereof", block.FirstAmount * multiplier),
        };

        if (block.IncrementAmount > 0m)
        {
            lines.Add(Line(
                "succeeding",
                SucceedingDescription(block),
                block.IncrementAmount * multiplier));
        }

        return lines;
    }

    private static string PerUnitDescription(RateBlock block)
        => block.PerUnit switch
        {
            IncrementUnit.Minute => "Per minute",
            IncrementUnit.Fraction => $"Every {block.FractionMinutes} minutes or part thereof",
            _ => "Every hour or part thereof",
        };

    private static string SucceedingDescription(RateBlock block)
        => block.IncrementUnit switch
        {
            IncrementUnit.Minute => "Succeeding minute",
            IncrementUnit.Fraction => $"Succeeding {block.FractionMinutes} minutes or part thereof",
            _ => "Succeeding hour or part thereof",
        };

    private static PricingLineItem Line(string code, string description, decimal amount)
        => new(code, description, Math.Round(amount, 2, MidpointRounding.AwayFromZero));
}

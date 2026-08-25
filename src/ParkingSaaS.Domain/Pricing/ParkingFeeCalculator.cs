using ParkingSaaS.Domain.Benefits;
using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Domain.Pricing;

public interface IParkingFeeCalculator
{
    /// <summary>Computes the fee for a stay. Pure and deterministic given the same input.</summary>
    FeeCalculationResult Calculate(FeeCalculationInput input);
}

/// <summary>
/// Configurable, versioned parking-fee calculator. All money is computed with
/// <see cref="decimal"/> and rounded to two places. Local-date rules (weekend,
/// holiday, overnight) are evaluated in the location's timezone; everything else
/// works on elapsed time. Hard-codes no fees — all behaviour comes from the rules.
/// </summary>
public sealed class ParkingFeeCalculator : IParkingFeeCalculator
{
    public FeeCalculationResult Calculate(FeeCalculationInput input)
    {
        var rules = input.Rules;
        var billedMinutes = (int)Math.Ceiling(Math.Max(0d, (input.CalculationTime - input.EntryTime).TotalMinutes));

        // Entry grace: a stay within the grace window is free.
        if (billedMinutes <= rules.EntryGraceMinutes || billedMinutes <= 0)
        {
            var breakdown = new List<PricingLineItem>();
            breakdown.Add(new PricingLineItem("entry_grace", $"Within {rules.EntryGraceMinutes}-minute entry grace", 0m));
            return Result(input, 0m, 0m, 0m, breakdown);
        }

        var normal = ComputeCharges(rules, input, billedMinutes, includeBreakdown: true);
        var subtotal = normal.BaseAmount + normal.AdditionalAmount;

        // A corporate benefit is a time entitlement, not a client-supplied
        // percentage/fixed discount. Calculate the remaining non-free elapsed
        // duration first, then round that duration once using the tariff's
        // existing minute billing rule. This is important for a stay that is
        // only a few seconds old: a fully covered stay must be free immediately,
        // while a stay that crosses the end of the window must still pay for
        // the non-free portion.
        if (input.FreeIntervals is { Count: > 0 })
        {
            var billableMinutes = BillableMinutesAfterBenefit(
                input.FreeIntervals, input.EntryTime, input.CalculationTime);
            // Keep stay-level surcharges (for example overnight) anchored to
            // the actual elapsed stay. The benefit discounts time-based
            // parking charges, but must not accidentally erase a surcharge
            // merely because free minutes were removed from the meter.
            var adjusted = ComputeCharges(rules, input, billableMinutes, includeBreakdown: false, surchargeMinutes: billedMinutes);
            var adjustedSubtotal = adjusted.BaseAmount + adjusted.AdditionalAmount;
            var benefitDiscount = Round(Math.Clamp(subtotal - adjustedSubtotal, 0m, subtotal));
            var breakdown = normal.Breakdown.ToList();
            breakdown.Add(new PricingLineItem("corporate_benefit", "Complimentary parking time", -benefitDiscount));
            return Result(input, normal.BaseAmount, normal.AdditionalAmount, benefitDiscount, breakdown, adjustedSubtotal);
        }

        var discountAmount = ComputeDiscount(subtotal, input.Discount, normal.Breakdown);
        var total = Round(Math.Max(0m, subtotal - discountAmount));
        return Result(input, normal.BaseAmount, normal.AdditionalAmount, discountAmount, normal.Breakdown, total);
    }

    private static ChargeResult ComputeCharges(
        PricingRules rules, FeeCalculationInput input, int billedMinutes, bool includeBreakdown, int? surchargeMinutes = null)
    {
        var breakdown = new List<PricingLineItem>();
        if (billedMinutes <= 0)
            return new ChargeResult(0m, 0m, breakdown);

        var localEntry = ToLocal(input.EntryTime, input.Timezone);
        var isWeekend = localEntry.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var isHoliday = rules.Holidays.Contains(localEntry.ToString("yyyy-MM-dd"));

        var block = SelectBlock(rules, input.VehicleType, isWeekend, isHoliday, out var blockLabel);
        var baseAmount = ComputeBlock(block, billedMinutes, includeBreakdown ? breakdown : new List<PricingLineItem>(), blockLabel);

        // Day/holiday multipliers applied after the base computation.
        if (isHoliday && rules.HolidayMultiplier is { } hm && hm != 1m)
        {
            var delta = Round(baseAmount * hm) - baseAmount;
            baseAmount = Round(baseAmount * hm);
            if (includeBreakdown) breakdown.Add(new PricingLineItem("holiday_multiplier", $"Holiday rate ×{hm}", delta));
        }
        else if (isWeekend && rules.WeekendMultiplier is { } wm && wm != 1m)
        {
            var delta = Round(baseAmount * wm) - baseAmount;
            baseAmount = Round(baseAmount * wm);
            if (includeBreakdown) breakdown.Add(new PricingLineItem("weekend_multiplier", $"Weekend rate ×{wm}", delta));
        }

        // Overnight surcharge if the stay overlaps the overnight window.
        var additionalAmount = 0m;
        if (rules.Overnight is { } overnight && overnight.Fee > 0m &&
            StayOverlapsOvernight(localEntry, surchargeMinutes ?? billedMinutes, overnight))
        {
            additionalAmount += overnight.Fee;
            if (includeBreakdown) breakdown.Add(new PricingLineItem("overnight", "Overnight surcharge", overnight.Fee));
        }

        return new ChargeResult(Round(baseAmount), Round(additionalAmount), breakdown);
    }

    private static int BillableMinutesAfterBenefit(
        IReadOnlyList<FreeTimeInterval>? intervals, DateTimeOffset from, DateTimeOffset until)
    {
        var totalSeconds = Math.Max(0d, (until - from).TotalSeconds);
        if (totalSeconds <= 0d) return 0;
        if (intervals is null || intervals.Count == 0)
            return (int)Math.Ceiling(totalSeconds / 60d);

        var merged = intervals
            .Select(i => (From: i.From < from ? from : i.From, To: i.To > until ? until : i.To))
            .Where(i => i.To > i.From)
            .OrderBy(i => i.From)
            .ToList();
        if (merged.Count == 0)
            return (int)Math.Ceiling(totalSeconds / 60d);

        var coveredSeconds = 0d;
        var current = merged[0];
        foreach (var next in merged.Skip(1))
        {
            if (next.From <= current.To)
            {
                if (next.To > current.To) current.To = next.To;
                continue;
            }

            coveredSeconds += (current.To - current.From).TotalSeconds;
            current = next;
        }
        coveredSeconds += (current.To - current.From).TotalSeconds;
        var billableSeconds = Math.Max(0d, totalSeconds - coveredSeconds);
        return (int)Math.Min(int.MaxValue, Math.Ceiling(billableSeconds / 60d));
    }

    private static RateBlock SelectBlock(PricingRules rules, VehicleType vehicleType, bool isWeekend, bool isHoliday, out string label)
    {
        if (isHoliday && rules.Holiday is not null) { label = "Holiday rate"; return rules.Holiday; }
        if (isWeekend && rules.Weekend is not null) { label = "Weekend rate"; return rules.Weekend; }
        if (rules.VehicleRates.TryGetValue(vehicleType.ToString(), out var vehicleBlock))
        {
            label = $"{vehicleType} rate";
            return vehicleBlock;
        }
        label = "Parking fee";
        return rules.Default;
    }

    private static decimal ComputeBlock(RateBlock block, int billedMinutes, List<PricingLineItem> breakdown, string label)
    {
        switch (block.Type)
        {
            case RateType.Flat:
                breakdown.Add(new PricingLineItem("flat", label, Round(block.FlatAmount)));
                return Round(block.FlatAmount);

            case RateType.PerUnit:
            {
                var units = UnitsFor(billedMinutes, block.PerUnit, block.FractionMinutes);
                var amount = Round(units * block.PerUnitAmount);
                breakdown.Add(new PricingLineItem("per_unit", $"{label}: {units} × {block.PerUnitAmount}", amount));
                return amount;
            }

            case RateType.FirstBlock:
            default:
            {
                var firstBlockMinutes = block.FirstHours * 60;
                var amount = Round(block.FirstAmount);
                breakdown.Add(new PricingLineItem("first_block",
                    $"{label}: first {block.FirstHours}h", amount));

                if (billedMinutes > firstBlockMinutes && block.IncrementAmount > 0m)
                {
                    var extraMinutes = billedMinutes - firstBlockMinutes;
                    var units = UnitsFor(extraMinutes, block.IncrementUnit, block.FractionMinutes);
                    var extra = Round(units * block.IncrementAmount);
                    amount += extra;
                    breakdown.Add(new PricingLineItem("succeeding",
                        $"Succeeding: {units} × {block.IncrementAmount}", extra));
                }
                return amount;
            }
        }
    }

    private static int UnitsFor(int minutes, IncrementUnit unit, int fractionMinutes) => unit switch
    {
        IncrementUnit.Minute => minutes,
        IncrementUnit.Fraction => (int)Math.Ceiling(minutes / (double)Math.Max(1, fractionMinutes)),
        _ => (int)Math.Ceiling(minutes / 60d) // Hour (or fraction thereof)
    };

    private static decimal ComputeDiscount(decimal subtotal, DiscountInput? discount, List<PricingLineItem> breakdown)
    {
        if (discount is null || discount.Type == DiscountType.None || subtotal <= 0m)
            return 0m;

        decimal amount = discount.Type switch
        {
            DiscountType.Complimentary => subtotal,
            _ when discount.IsPercentage => Round(subtotal * (discount.Value / 100m)),
            _ => Round(discount.Value)
        };
        amount = Math.Clamp(amount, 0m, subtotal);

        var label = discount.Type switch
        {
            DiscountType.Complimentary => "Complimentary parking",
            DiscountType.SeniorPwd => "Senior/PWD discount",
            DiscountType.Merchant => "Merchant validation",
            _ => "Discount"
        };
        breakdown.Add(new PricingLineItem("discount", label, -amount));
        return amount;
    }

    private static bool StayOverlapsOvernight(DateTime localEntry, int billedMinutes, OvernightRule rule)
    {
        // Walk each minute boundary day; simpler: check if any point of the stay falls in the window.
        var localExit = localEntry.AddMinutes(billedMinutes);
        for (var day = localEntry.Date; day <= localExit.Date; day = day.AddDays(1))
        {
            var windowStart = day.AddHours(rule.StartHour);
            // Overnight windows cross midnight when EndHour <= StartHour.
            var windowEnd = rule.EndHour <= rule.StartHour
                ? day.AddDays(1).AddHours(rule.EndHour)
                : day.AddHours(rule.EndHour);

            if (localEntry < windowEnd && localExit > windowStart)
                return true;
        }
        return false;
    }

    private static DateTime ToLocal(DateTimeOffset utc, string timezone)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return TimeZoneInfo.ConvertTime(utc, tz).DateTime;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return utc.UtcDateTime;
        }
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record ChargeResult(decimal BaseAmount, decimal AdditionalAmount, List<PricingLineItem> Breakdown);

    private static FeeCalculationResult Result(
        FeeCalculationInput input, decimal baseAmount, decimal additional, decimal discount,
        List<PricingLineItem> breakdown, decimal? total = null)
        => new(
            input.EntryTime,
            input.CalculationTime,
            input.Rules.Currency,
            Round(baseAmount),
            Round(additional),
            Round(discount),
            total ?? Round(Math.Max(0m, baseAmount + additional - discount)),
            breakdown,
            input.RatePlanVersionId,
            input.RatePlanVersionNumber);
}

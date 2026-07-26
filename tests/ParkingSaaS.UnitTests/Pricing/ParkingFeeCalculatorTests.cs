using FluentAssertions;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.Sessions;
using Xunit;

namespace ParkingSaaS.UnitTests.Pricing;

public sealed class ParkingFeeCalculatorTests
{
    private readonly ParkingFeeCalculator _calc = new();
    private static readonly DateTimeOffset Entry = new(2026, 6, 24, 10, 0, 0, TimeSpan.FromHours(8)); // Wed, Manila

    private FeeCalculationResult Run(PricingRules rules, int minutes, VehicleType vehicle = VehicleType.Car, DiscountInput? discount = null)
        => _calc.Calculate(new FeeCalculationInput(
            Entry, Entry.AddMinutes(minutes), vehicle, Guid.NewGuid(), 1, rules, "Asia/Manila", discount));

    private static PricingRules CanonicalRules() => new()
    {
        Currency = "PHP",
        EntryGraceMinutes = 15,
        PaidExitGraceMinutes = 15,
        Default = new RateBlock
        {
            Type = RateType.FirstBlock,
            FirstHours = 3,
            FirstAmount = 50m,
            IncrementAmount = 20m,
            IncrementUnit = IncrementUnit.Hour
        }
    };

    // ---- Entry grace -------------------------------------------------------
    [Fact]
    public void Within_entry_grace_is_free()
        => Run(CanonicalRules(), minutes: 10).TotalAmount.Should().Be(0m);

    // ---- First-block + succeeding (the brief's canonical example) ----------
    [Theory]
    [InlineData(120, 50)]   // 2h → first block
    [InlineData(180, 50)]   // exactly 3h → first block
    [InlineData(181, 70)]   // 3h1m → +1 hour-or-fraction
    [InlineData(240, 70)]   // 4h → +1 hour
    [InlineData(241, 90)]   // 4h1m → +2 hours-or-fraction
    public void First_block_then_succeeding_per_hour_or_fraction(int minutes, decimal expected)
        => Run(CanonicalRules(), minutes).TotalAmount.Should().Be(expected);

    [Fact]
    public void No_daily_maximum_is_applied()
        => Run(CanonicalRules(), 14 * 60).TotalAmount.Should().Be(270m);

    [Fact]
    public void Long_stay_charges_each_succeeding_hour_instead_of_capping_at_a_daily_amount()
    {
        var rules = CanonicalRules();
        rules.Default.FirstAmount = 250m;
        rules.Default.IncrementAmount = 50m;

        // First 3 hours = PHP 250, then 10 started succeeding hours at
        // PHP 50/hour for a 12h11m stay. A legacy PHP 250 daily cap would
        // incorrectly return only PHP 250 here.
        var result = Run(rules, 12 * 60 + 11);

        result.TotalAmount.Should().Be(750m);
        result.Breakdown.Should().Contain(item => item.Code == "succeeding" && item.Amount == 500m);
    }

    // ---- Flat --------------------------------------------------------------
    [Fact]
    public void Flat_rate_charges_a_single_amount()
    {
        var rules = new PricingRules { Default = new RateBlock { Type = RateType.Flat, FlatAmount = 100m } };
        Run(rules, 500).TotalAmount.Should().Be(100m);
    }

    // ---- Per-hour ----------------------------------------------------------
    [Theory]
    [InlineData(60, 30)]
    [InlineData(61, 60)]   // 2 hours-or-fraction
    [InlineData(150, 90)]  // 3 hours-or-fraction
    public void Per_hour_charges_each_started_hour(int minutes, decimal expected)
    {
        var rules = new PricingRules { Default = new RateBlock { Type = RateType.PerUnit, PerUnit = IncrementUnit.Hour, PerUnitAmount = 30m } };
        Run(rules, minutes).TotalAmount.Should().Be(expected);
    }

    // ---- Per-minute --------------------------------------------------------
    [Fact]
    public void Per_minute_charges_each_minute()
    {
        var rules = new PricingRules { Default = new RateBlock { Type = RateType.PerUnit, PerUnit = IncrementUnit.Minute, PerUnitAmount = 0.5m } };
        Run(rules, 80).TotalAmount.Should().Be(40m);
    }

    // ---- Per-fraction ------------------------------------------------------
    [Theory]
    [InlineData(30, 10)]
    [InlineData(31, 20)]   // ceil(31/30) = 2 fractions
    [InlineData(90, 30)]
    public void Per_fraction_charges_each_started_fraction(int minutes, decimal expected)
    {
        var rules = new PricingRules { Default = new RateBlock { Type = RateType.PerUnit, PerUnit = IncrementUnit.Fraction, FractionMinutes = 30, PerUnitAmount = 10m } };
        Run(rules, minutes).TotalAmount.Should().Be(expected);
    }

    // ---- Vehicle-type rates ------------------------------------------------
    [Fact]
    public void Vehicle_type_rate_overrides_the_default()
    {
        var rules = CanonicalRules();
        rules.VehicleRates["Motorcycle"] = new RateBlock { Type = RateType.Flat, FlatAmount = 20m };

        Run(rules, 240, VehicleType.Motorcycle).TotalAmount.Should().Be(20m); // flat
        Run(rules, 240, VehicleType.Car).TotalAmount.Should().Be(70m);        // default
    }

    // ---- Weekend -----------------------------------------------------------
    [Fact]
    public void Weekend_multiplier_applies_on_weekends()
    {
        var rules = CanonicalRules();
        rules.WeekendMultiplier = 1.5m;
        var saturday = new DateTimeOffset(2026, 6, 27, 10, 0, 0, TimeSpan.FromHours(8));

        var result = _calc.Calculate(new FeeCalculationInput(
            saturday, saturday.AddHours(2), VehicleType.Car, Guid.NewGuid(), 1, rules, "Asia/Manila", null));

        result.TotalAmount.Should().Be(75m); // 50 * 1.5
    }

    // ---- Holiday -----------------------------------------------------------
    [Fact]
    public void Holiday_multiplier_takes_precedence_over_weekend()
    {
        var rules = CanonicalRules();
        rules.WeekendMultiplier = 1.5m;
        rules.HolidayMultiplier = 2m;
        rules.Holidays.Add("2026-06-13"); // a Saturday that is also a holiday

        var entry = new DateTimeOffset(2026, 6, 13, 10, 0, 0, TimeSpan.FromHours(8));
        var result = _calc.Calculate(new FeeCalculationInput(
            entry, entry.AddHours(2), VehicleType.Car, Guid.NewGuid(), 1, rules, "Asia/Manila", null));

        result.TotalAmount.Should().Be(100m); // 50 * 2 (holiday), not *1.5
    }

    // ---- Overnight ---------------------------------------------------------
    [Fact]
    public void Overnight_surcharge_applies_when_stay_overlaps_window()
    {
        var rules = CanonicalRules();
        rules.Overnight = new OvernightRule { Fee = 80m, StartHour = 22, EndHour = 6 };
        var entry = new DateTimeOffset(2026, 6, 24, 21, 0, 0, TimeSpan.FromHours(8)); // 9pm local

        var result = _calc.Calculate(new FeeCalculationInput(
            entry, entry.AddHours(3), VehicleType.Car, Guid.NewGuid(), 1, rules, "Asia/Manila", null));

        result.AdditionalAmount.Should().Be(80m);
        result.TotalAmount.Should().Be(130m); // 50 base (3h) + 80 overnight
    }

    [Fact]
    public void Overnight_surcharge_does_not_apply_to_daytime_stay()
    {
        var rules = CanonicalRules();
        rules.Overnight = new OvernightRule { Fee = 80m, StartHour = 22, EndHour = 6 };
        Run(rules, 120).AdditionalAmount.Should().Be(0m); // 10:00–12:00 local
    }

    // ---- Discounts ---------------------------------------------------------
    [Fact]
    public void Senior_pwd_percentage_discount_is_applied()
    {
        var result = Run(CanonicalRules(), 241, discount: new DiscountInput(DiscountType.SeniorPwd, 20m, IsPercentage: true));
        result.DiscountAmount.Should().Be(18m); // 20% of 90
        result.TotalAmount.Should().Be(72m);
    }

    [Fact]
    public void Merchant_fixed_discount_is_applied_and_clamped()
    {
        var result = Run(CanonicalRules(), 120, discount: new DiscountInput(DiscountType.Merchant, 80m, IsPercentage: false));
        result.DiscountAmount.Should().Be(50m); // clamped to the 50 subtotal
        result.TotalAmount.Should().Be(0m);
    }

    [Fact]
    public void Complimentary_makes_the_total_zero()
    {
        var result = Run(CanonicalRules(), 240, discount: new DiscountInput(DiscountType.Complimentary, 0m, false));
        result.TotalAmount.Should().Be(0m);
        result.DiscountAmount.Should().Be(70m);
    }

    // ---- Result shape ------------------------------------------------------
    [Fact]
    public void Result_carries_breakdown_and_rate_plan_version()
    {
        var versionId = Guid.NewGuid();
        var result = _calc.Calculate(new FeeCalculationInput(
            Entry, Entry.AddMinutes(241), VehicleType.Car, versionId, 7, CanonicalRules(), "Asia/Manila", null));

        result.RatePlanVersionId.Should().Be(versionId);
        result.RatePlanVersionNumber.Should().Be(7);
        result.Currency.Should().Be("PHP");
        result.Breakdown.Should().NotBeEmpty();
        result.Breakdown.Sum(i => i.Amount).Should().Be(result.TotalAmount);
    }
}

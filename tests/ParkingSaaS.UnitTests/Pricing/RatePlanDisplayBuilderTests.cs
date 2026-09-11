using FluentAssertions;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.Sessions;
using Xunit;

namespace ParkingSaaS.UnitTests.Pricing;

public sealed class RatePlanDisplayBuilderTests
{
    private static readonly DateTimeOffset Weekday = new(2026, 6, 24, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Uses_the_configured_first_block_duration_and_increment_wording()
    {
        var rules = new PricingRules
        {
            Default = new RateBlock
            {
                Type = RateType.FirstBlock,
                FirstHours = 3,
                FirstAmount = 50m,
                IncrementAmount = 20m,
                IncrementUnit = IncrementUnit.Hour,
            },
        };

        var lines = RatePlanDisplayBuilder.Build(rules, VehicleType.Car, Weekday, "Asia/Manila");

        lines.Should().Equal(
            new PricingLineItem("first_block", "First 3 hours or part thereof", 50m),
            new PricingLineItem("succeeding", "Succeeding hour or part thereof", 20m));
    }

    [Fact]
    public void Uses_the_vehicle_rate_and_applies_the_applicable_holiday_multiplier()
    {
        var rules = new PricingRules
        {
            Default = new RateBlock { Type = RateType.Flat, FlatAmount = 500m },
            VehicleRates = new Dictionary<string, RateBlock>
            {
                [nameof(VehicleType.Motorcycle)] = new RateBlock { Type = RateType.Flat, FlatAmount = 20m },
            },
            Holiday = new RateBlock
            {
                Type = RateType.FirstBlock,
                FirstHours = 3,
                FirstAmount = 200m,
                IncrementAmount = 80m,
                IncrementUnit = IncrementUnit.Hour,
            },
            HolidayMultiplier = 1.5m,
            Holidays = new List<string> { "2026-06-24" },
        };

        var lines = RatePlanDisplayBuilder.Build(rules, VehicleType.Motorcycle, Weekday, "Asia/Manila");

        lines.Should().Equal(
            new PricingLineItem("first_block", "First 3 hours or part thereof", 300m),
            new PricingLineItem("succeeding", "Succeeding hour or part thereof", 120m));
    }

    [Fact]
    public void Uses_unit_specific_wording_for_per_minute_rates()
    {
        var rules = new PricingRules
        {
            Default = new RateBlock
            {
                Type = RateType.PerUnit,
                PerUnit = IncrementUnit.Minute,
                PerUnitAmount = 0.5m,
            },
        };

        var lines = RatePlanDisplayBuilder.Build(rules, VehicleType.Car, Weekday, "Asia/Manila");

        lines.Should().Equal(new PricingLineItem("per_unit", "Per minute", 0.5m));
    }
}

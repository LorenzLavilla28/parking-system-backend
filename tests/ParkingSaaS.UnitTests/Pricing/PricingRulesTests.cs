using FluentAssertions;
using ParkingSaaS.Domain.Pricing;
using Xunit;

namespace ParkingSaaS.UnitTests.Pricing;

public sealed class PricingRulesTests
{
    [Fact]
    public void Parses_json_with_string_enums()
    {
        const string json = """
        {
          "currency": "PHP",
          "entryGraceMinutes": 15,
          "default": { "type": "FirstBlock", "firstHours": 3, "firstAmount": 50, "incrementAmount": 20, "incrementUnit": "Hour" },
          "dailyMax": 250
        }
        """;

        var rules = PricingRules.Parse(json);
        rules.Currency.Should().Be("PHP");
        rules.EntryGraceMinutes.Should().Be(15);
        rules.Default.Type.Should().Be(RateType.FirstBlock);
        rules.Default.IncrementUnit.Should().Be(IncrementUnit.Hour);
        rules.DailyMax.Should().Be(250m);
    }

    [Fact]
    public void Serialize_then_parse_round_trips()
    {
        var rules = new PricingRules
        {
            Currency = "PHP",
            EntryGraceMinutes = 10,
            Default = new RateBlock { Type = RateType.PerUnit, PerUnit = IncrementUnit.Fraction, FractionMinutes = 30, PerUnitAmount = 12.5m }
        };

        var parsed = PricingRules.Parse(rules.Serialize());
        parsed.Default.PerUnitAmount.Should().Be(12.5m);
        parsed.Default.FractionMinutes.Should().Be(30);
    }

    [Fact]
    public void Valid_rules_report_no_errors()
        => new PricingRules { Currency = "PHP", Default = new RateBlock { FlatAmount = 50m } }
            .Validate().Should().BeEmpty();

    [Fact]
    public void Negative_amounts_and_bad_currency_are_reported()
    {
        var rules = new PricingRules
        {
            Currency = "PESO",
            Default = new RateBlock { FlatAmount = -1m, FractionMinutes = 0 }
        };

        var errors = rules.Validate();
        errors.Should().Contain(e => e.Contains("Currency"));
        errors.Should().Contain(e => e.Contains("amounts"));
        errors.Should().Contain(e => e.Contains("FractionMinutes"));
    }
}

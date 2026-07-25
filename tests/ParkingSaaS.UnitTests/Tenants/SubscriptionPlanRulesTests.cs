using FluentAssertions;
using ParkingSaaS.Domain.Tenants;
using Xunit;

namespace ParkingSaaS.UnitTests.Tenants;

public sealed class SubscriptionPlanRulesTests
{
    [Theory]
    [InlineData(SubscriptionPlan.Starter, 1, 20, 3000)]
    [InlineData(SubscriptionPlan.Growth, 2, 50, 6000)]
    [InlineData(SubscriptionPlan.Enterprise, 3, 90, 10000)]
    public void Returns_the_paid_plan_entitlements(SubscriptionPlan plan, int locations, int slots, decimal monthlyPrice)
    {
        var result = SubscriptionPlanRules.For(plan);

        result.MaximumLocations.Should().Be(locations);
        result.MaximumSlotsPerLocation.Should().Be(slots);
        result.MonthlyPrice.Should().Be(monthlyPrice);
    }

    [Theory]
    [InlineData(20, 3000)]
    [InlineData(21, 6000)]
    [InlineData(50, 6000)]
    [InlineData(51, 10000)]
    [InlineData(90, 10000)]
    public void Prices_addons_by_capacity_band(int slots, decimal monthlyPrice)
        => SubscriptionPlanRules.AddOnPriceFor(slots).Should().Be(monthlyPrice);

    [Fact]
    public void Capacity_above_90_requires_custom_pricing()
        => SubscriptionPlanRules.AddOnPriceFor(91).Should().BeNull();
}

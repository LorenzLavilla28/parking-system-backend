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

    [Fact]
    public void Additional_capacity_extends_the_plan_limit_per_location()
        => SubscriptionPlanRules.EffectiveMaximumSlotsPerLocation(SubscriptionPlan.Growth, 20).Should().Be(70);

    [Theory]
    [InlineData(SubscriptionPlan.Starter, 10, 0, 1500)]
    [InlineData(SubscriptionPlan.Growth, 50, 0, 6000)]
    [InlineData(SubscriptionPlan.Enterprise, 45, 0, 5000)]
    [InlineData(SubscriptionPlan.Starter, 20, 5, 3750)]
    public void Calculates_monthly_price_from_purchased_capacity(
        SubscriptionPlan plan,
        int purchasedCapacity,
        int additionalCapacity,
        decimal expectedPrice)
        => SubscriptionPlanRules.MonthlyPrice(plan, purchasedCapacity, additionalCapacity, true).Should().Be(expectedPrice);

    [Fact]
    public void Legacy_fixed_pricing_is_preserved_until_capacity_pricing_is_enabled()
        => SubscriptionPlanRules.MonthlyPrice(SubscriptionPlan.Growth, 50, 20, false).Should().Be(6000);
}

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
}

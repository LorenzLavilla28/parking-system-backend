namespace ParkingSaaS.Domain.Tenants;

/// <summary>
/// Paid-at-onboarding subscription entitlements. Prices are PHP monthly list
/// prices for the current phase; recurring renewal is intentionally out of scope.
/// Slot limits apply independently to every location in the tenant.
/// </summary>
public static class SubscriptionPlanRules
{
    public static SubscriptionPlanLimits For(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Starter => new(plan, 1, 20, 3000m),
        SubscriptionPlan.Growth => new(plan, 2, 50, 6000m),
        SubscriptionPlan.Enterprise => new(plan, 3, 90, 10000m),
        SubscriptionPlan.Custom => new(plan, null, null, null),
        // Retained for existing tenants during migration. New onboarding should
        // use one of the paid plans above.
        SubscriptionPlan.Free => new(plan, 1, 20, 0m),
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unknown subscription plan.")
    };

    public static bool IsPaidPlan(SubscriptionPlan plan) => plan is not SubscriptionPlan.Free;

    public static int? EffectiveMaximumSlotsPerLocation(SubscriptionPlan plan, int additionalSlotCapacity)
    {
        var baseLimit = For(plan).MaximumSlotsPerLocation;
        return baseLimit is null ? null : baseLimit.Value + Math.Max(0, additionalSlotCapacity);
    }

    public static int? EffectiveMaximumSlotsPerLocation(
        SubscriptionPlan plan,
        int additionalSlotCapacity,
        int? purchasedSlotCapacityPerLocation,
        bool capacityPricingEnabled)
    {
        if (capacityPricingEnabled && purchasedSlotCapacityPerLocation is { } purchasedCapacity)
            return purchasedCapacity + Math.Max(0, additionalSlotCapacity);

        return EffectiveMaximumSlotsPerLocation(plan, additionalSlotCapacity);
    }

    public static decimal? PricePerSlot(SubscriptionPlan plan)
    {
        var limits = For(plan);
        return limits.MaximumSlotsPerLocation is { } slots && limits.MonthlyPrice is { } price
            ? price / slots
            : null;
    }

    public static decimal? MonthlyPrice(
        SubscriptionPlan plan,
        int? purchasedSlotCapacityPerLocation,
        int additionalSlotCapacity,
        bool capacityPricingEnabled)
    {
        var limits = For(plan);
        if (!capacityPricingEnabled || purchasedSlotCapacityPerLocation is null || limits.MaximumSlotsPerLocation is null || limits.MonthlyPrice is null)
            return limits.MonthlyPrice;

        var totalCapacity = purchasedSlotCapacityPerLocation.Value + Math.Max(0, additionalSlotCapacity);
        return decimal.Round(totalCapacity * limits.MonthlyPrice.Value / limits.MaximumSlotsPerLocation.Value, 2, MidpointRounding.AwayFromZero);
    }

}

public sealed record SubscriptionPlanLimits(
    SubscriptionPlan Plan,
    int? MaximumLocations,
    int? MaximumSlotsPerLocation,
    decimal? MonthlyPrice);

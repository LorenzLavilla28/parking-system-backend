using ParkingSaaS.Domain.Pricing;

namespace ParkingSaaS.UnitTests.Common;

/// <summary>Builds a minimal <see cref="FeeCalculationResult"/> for tests that only care about the total.</summary>
internal static class FeeResults
{
    public static FeeCalculationResult Of(decimal total, string currency = "PHP")
        => new(
            DateTimeOffset.UtcNow.AddHours(-4),
            DateTimeOffset.UtcNow,
            currency,
            total,
            0m,
            0m,
            total,
            new[] { new PricingLineItem("fee", "Parking fee", total) },
            Guid.NewGuid(),
            1);
}

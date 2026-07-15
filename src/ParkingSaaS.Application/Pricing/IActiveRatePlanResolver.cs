namespace ParkingSaaS.Application.Pricing;

/// <summary>Resolves which rate plan version applies to a location at a moment in time.</summary>
public interface IActiveRatePlanResolver
{
    /// <summary>The effective version id for an active plan at the location, or null if none.</summary>
    Task<Guid?> ResolveActiveVersionIdAsync(Guid parkingLocationId, DateTimeOffset at, CancellationToken ct);
}

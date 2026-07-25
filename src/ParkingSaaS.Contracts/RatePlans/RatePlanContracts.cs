namespace ParkingSaaS.Contracts.RatePlans;

/// <summary>Creates a rate plan with its first version. The plan is activated immediately.</summary>
public sealed record CreateRatePlanRequest(
    string Name,
    string Description,
    string RulesJson);

public sealed record AddRatePlanVersionRequest(string RulesJson);

public sealed record RatePlanResponse(
    Guid Id,
    Guid? ParkingLocationId,
    string Name,
    string Description,
    string Status,
    int? CurrentVersionNumber,
    int? PaidExitGraceMinutes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? CurrentRulesJson = null);

public sealed record RatePlanVersionResponse(
    Guid Id,
    Guid RatePlanId,
    int VersionNumber,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    string RulesJson,
    DateTimeOffset CreatedAt);

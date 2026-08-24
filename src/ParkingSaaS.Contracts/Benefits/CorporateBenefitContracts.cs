namespace ParkingSaaS.Contracts.Benefits;

public sealed record CorporateBenefitWindowRequest(string Start, string End);

public sealed record CorporateBenefitRulesRequest(
    IReadOnlyList<CorporateBenefitWindowRequest> Windows,
    IReadOnlyList<string> DaysOfWeek,
    IReadOnlyList<string> Holidays,
    bool ExcludeHolidays,
    IReadOnlyList<string> EligibleVehicleTypes,
    string OutsideWindowBehavior = "NormalRate");

public sealed record CorporateBenefitLocationRequest(Guid ParkingLocationId, int MaxConcurrentFreeSessions = 1);

public sealed record CreateCorporateBenefitRequest(
    string Name,
    string? Description,
    int Priority,
    IReadOnlyList<CorporateBenefitLocationRequest> Locations,
    CorporateBenefitRulesRequest Rules,
    IReadOnlyList<string> PlateNumbers,
    DateTimeOffset? EffectiveFrom = null,
    DateTimeOffset? EffectiveTo = null);

public sealed record UpdateCorporateBenefitRequest(
    string Name,
    string? Description,
    int Priority,
    IReadOnlyList<CorporateBenefitLocationRequest> Locations,
    CorporateBenefitRulesRequest Rules,
    IReadOnlyList<string> PlateNumbers,
    DateTimeOffset? EffectiveFrom = null,
    DateTimeOffset? EffectiveTo = null);

public sealed record CorporateBenefitProgramResponse(
    Guid Id,
    string Name,
    string Description,
    int Priority,
    string Status,
    int CurrentVersionNumber,
    IReadOnlyList<CorporateBenefitLocationResponse> Locations,
    CorporateBenefitRulesRequest Rules,
    IReadOnlyList<CorporateBenefitPlateResponse> Plates,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CurrentEffectiveFrom = null,
    DateTimeOffset? CurrentEffectiveTo = null);

public sealed record CorporateBenefitLocationResponse(
    Guid ParkingLocationId,
    string LocationName,
    int MaxConcurrentFreeSessions,
    int ActiveAllocations);

public sealed record CorporateBenefitPlateResponse(
    Guid Id,
    string PlateNumber,
    bool IsActive,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo);

public sealed record CorporateBenefitAllocationResponse(
    Guid Id,
    Guid CorporateBenefitProgramId,
    Guid ParkingLocationId,
    Guid ParkingSessionId,
    string PlateNumber,
    DateTimeOffset AllocatedAt,
    DateTimeOffset? ReleasedAt,
    string Status);

public sealed record CorporateBenefitAvailabilityResponse(
    Guid CorporateBenefitProgramId,
    Guid ParkingLocationId,
    int Capacity,
    int ActiveAllocations,
    int AvailableSlots);

public sealed record SetCorporateBenefitStatusRequest(string Status);

public sealed record BenefitApplicationResult(
    bool Applied,
    string? ProgramName,
    string? Message);

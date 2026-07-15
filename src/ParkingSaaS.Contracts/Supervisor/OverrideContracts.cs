namespace ParkingSaaS.Contracts.Supervisor;

public sealed record VoidSessionRequest(Guid SessionId, string Reason, string? DeviceInformation);
public sealed record ComplimentaryRequest(Guid SessionId, string Reason, string? DeviceInformation);
public sealed record WaiveOutstandingRequest(Guid SessionId, string Reason, string? DeviceInformation);
public sealed record CorrectEntryTimeRequest(Guid SessionId, DateTimeOffset NewEntryTime, string Reason, string? DeviceInformation);
public sealed record CorrectPlateRequest(Guid SessionId, string NewPlateNumber, string Reason, string? DeviceInformation);

/// <summary>Outcome of a supervisor override, including the resulting session state.</summary>
public sealed record OverrideResponse(
    Guid SessionId,
    string Action,
    string Status,
    string PlateNumberRaw,
    decimal? FeeOverride,
    DateTimeOffset EntryTime);

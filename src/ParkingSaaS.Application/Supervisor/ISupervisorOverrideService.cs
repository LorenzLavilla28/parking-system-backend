using ParkingSaaS.Contracts.Supervisor;

namespace ParkingSaaS.Application.Supervisor;

/// <summary>
/// Supervisor/tenant-admin manual overrides. Every operation requires a reason and
/// writes an audit log with old/new values.
/// </summary>
public interface ISupervisorOverrideService
{
    Task<OverrideResponse> VoidAsync(VoidSessionRequest request, string? ipAddress, CancellationToken ct);
    Task<OverrideResponse> MarkComplimentaryAsync(ComplimentaryRequest request, string? ipAddress, CancellationToken ct);
    Task<OverrideResponse> WaiveOutstandingAsync(WaiveOutstandingRequest request, string? ipAddress, CancellationToken ct);
    Task<OverrideResponse> CorrectEntryTimeAsync(CorrectEntryTimeRequest request, string? ipAddress, CancellationToken ct);
    Task<OverrideResponse> CorrectPlateAsync(CorrectPlateRequest request, string? ipAddress, CancellationToken ct);
}

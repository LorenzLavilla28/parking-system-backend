using ParkingSaaS.Contracts.Benefits;
using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Application.Benefits;

public interface ICorporateBenefitService
{
    Task<IReadOnlyList<CorporateBenefitProgramResponse>> ListAsync(CancellationToken ct);
    Task<CorporateBenefitProgramResponse> GetAsync(Guid id, CancellationToken ct);
    Task<CorporateBenefitProgramResponse> CreateAsync(CreateCorporateBenefitRequest request, CancellationToken ct);
    Task<CorporateBenefitProgramResponse> UpdateAsync(Guid id, UpdateCorporateBenefitRequest request, CancellationToken ct);
    Task SetStatusAsync(Guid id, string status, CancellationToken ct);
    Task<IReadOnlyList<CorporateBenefitAllocationResponse>> ListAllocationsAsync(Guid id, CancellationToken ct);
    Task<CorporateBenefitAvailabilityResponse> GetAvailabilityAsync(Guid id, Guid locationId, CancellationToken ct);
    Task<IReadOnlyList<GuardCorporateBenefitOptionResponse>> ListGuardOptionsAsync(Guid locationId, string vehicleType, CancellationToken ct);
}

public interface ICorporateBenefitAllocationService
{
    Task<BenefitAllocationDecision> TryAllocateAsync(
        Guid tenantId, Guid parkingLocationId, Guid parkingSessionId,
        VehicleType vehicleType, DateTimeOffset at, CancellationToken ct,
        Guid? selectedProgramId = null);

    Task ReleaseForSessionAsync(Guid sessionId, DateTimeOffset at, CancellationToken ct);
}

public sealed record BenefitAllocationDecision(
    bool Applied,
    string? ProgramName,
    string? Message,
    Guid? AllocationId);

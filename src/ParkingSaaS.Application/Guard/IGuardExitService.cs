using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Application.Guard;

public interface IGuardExitService
{
    /// <summary>Computes the large exit-status banner (Paid / NotPaid / AdditionalPaymentRequired / ...).</summary>
    Task<ExitStatusResponse> GetExitStatusAsync(Guid sessionId, CancellationToken ct);

    /// <summary>Server-revalidated exit approval. Records exit and writes an audit log.</summary>
    Task<ExitApprovedResponse> ApproveExitAsync(ApproveExitRequest request, string? ipAddress, CancellationToken ct);
}

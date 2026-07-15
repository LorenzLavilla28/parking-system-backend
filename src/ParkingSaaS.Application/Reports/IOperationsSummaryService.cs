using ParkingSaaS.Contracts.Reports;

namespace ParkingSaaS.Application.Reports;

public interface IOperationsSummaryService
{
    Task<OperationsSummaryResponse> GetCurrentAsync(int hours, CancellationToken ct);

    Task<OperationsSummaryEmailResponse> QueueCurrentEmailAsync(int hours, CancellationToken ct);

    Task<int> QueueScheduledEmailsAsync(int hours, CancellationToken ct);
}

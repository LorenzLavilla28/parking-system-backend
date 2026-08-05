using ParkingSaaS.Contracts.Reports;

namespace ParkingSaaS.Application.Reports;

public interface IOperationsSummaryService
{
    Task<OperationsSummaryResponse> GetCurrentAsync(int hours, CancellationToken ct);

    Task<OperationsSummaryEmailResponse> QueueCurrentEmailAsync(int hours, CancellationToken ct);

    Task<OperationsSummarySettingsResponse> GetSettingsAsync(CancellationToken ct);

    Task<OperationsSummarySettingsResponse> UpdateSettingsAsync(
        UpdateOperationsSummarySettingsRequest request,
        CancellationToken ct);

    Task<int> QueueScheduledEmailsAsync(CancellationToken ct);
}

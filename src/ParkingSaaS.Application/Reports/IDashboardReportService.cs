using ParkingSaaS.Contracts.Reports;

namespace ParkingSaaS.Application.Reports;

public interface IDashboardReportService
{
    Task<DashboardReportResponse> GetAsync(
        int days,
        Guid? parkingLocationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct);
}

using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Application.Guard;

public interface IGuardSessionService
{
    Task<PagedResult<SessionSummaryResponse>> SearchAsync(
        string? plate, string? status, Guid? parkingLocationId, bool activeOnly, PageQuery page, CancellationToken ct);

    Task<SessionSummaryResponse> GetAsync(Guid id, CancellationToken ct);

    Task<SessionQrResponse> GetQrAsync(Guid id, CancellationToken ct);
}

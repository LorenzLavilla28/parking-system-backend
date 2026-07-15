using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.RatePlans;

namespace ParkingSaaS.Application.RatePlans;

public interface IRatePlanService
{
    Task<RatePlanResponse> CreateAsync(CreateRatePlanRequest request, CancellationToken ct);
    Task<RatePlanVersionResponse> AddVersionAsync(Guid ratePlanId, AddRatePlanVersionRequest request, CancellationToken ct);
    Task<RatePlanResponse> GetAsync(Guid id, CancellationToken ct);
    Task<PagedResult<RatePlanResponse>> ListAsync(Guid? parkingLocationId, PageQuery page, CancellationToken ct);
    Task<IReadOnlyList<RatePlanVersionResponse>> ListVersionsAsync(Guid ratePlanId, CancellationToken ct);
    Task ArchiveAsync(Guid id, CancellationToken ct);
}

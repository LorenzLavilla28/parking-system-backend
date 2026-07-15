using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Locations;

namespace ParkingSaaS.Application.Locations;

public interface ILocationService
{
    Task<LocationResponse> CreateAsync(CreateLocationRequest request, CancellationToken ct);
    Task<LocationResponse> UpdateAsync(Guid id, UpdateLocationRequest request, CancellationToken ct);
    Task<LocationResponse> GetAsync(Guid id, CancellationToken ct);
    Task<PagedResult<LocationResponse>> ListAsync(PageQuery query, CancellationToken ct);
    Task ArchiveAsync(Guid id, CancellationToken ct);
}

using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Users;

namespace ParkingSaaS.Application.Users;

public interface IUserService
{
    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct);
    Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct);
    Task<UserResponse> GetAsync(Guid id, CancellationToken ct);
    Task<PagedResult<UserResponse>> ListAsync(PageQuery query, CancellationToken ct);
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Users;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Users;

namespace ParkingSaaS.Api.Controllers;

/// <summary>Tenant administration of staff (supervisors and guards).</summary>
[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
[Route("api/tenant/users")]
public sealed class UsersController : ApiControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users) => _users = users;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageQuery query, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<UserResponse>>.Ok(await _users.ListAsync(query, ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<UserResponse>.Ok(await _users.GetAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var created = await _users.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse<UserResponse>.Ok(created));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest request, CancellationToken ct)
        => Ok(ApiResponse<UserResponse>.Ok(await _users.UpdateAsync(id, request, ct)));
}

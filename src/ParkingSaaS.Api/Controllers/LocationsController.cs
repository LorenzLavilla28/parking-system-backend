using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Locations;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Locations;

namespace ParkingSaaS.Api.Controllers;

/// <summary>
/// Tenant administration of parking locations. All operations are implicitly
/// tenant-scoped through the EF global filter; the controller never sees or
/// accepts a tenant id.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
[Route("api/tenant/locations")]
public sealed class LocationsController : ApiControllerBase
{
    private readonly ILocationService _locations;

    public LocationsController(ILocationService locations) => _locations = locations;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageQuery query, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<LocationResponse>>.Ok(await _locations.ListAsync(query, ct)));

    [HttpGet("quota")]
    public async Task<IActionResult> Quota(CancellationToken ct)
        => Ok(ApiResponse<LocationQuotaResponse>.Ok(await _locations.GetQuotaAsync(ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<LocationResponse>.Ok(await _locations.GetAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLocationRequest request, CancellationToken ct)
    {
        var created = await _locations.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse<LocationResponse>.Ok(created));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateLocationRequest request, CancellationToken ct)
        => Ok(ApiResponse<LocationResponse>.Ok(await _locations.UpdateAsync(id, request, ct)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await _locations.ArchiveAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        await _locations.RestoreAsync(id, ct);
        return NoContent();
    }
}

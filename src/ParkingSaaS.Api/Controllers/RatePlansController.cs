using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.RatePlans;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.RatePlans;

namespace ParkingSaaS.Api.Controllers;

/// <summary>Tenant-admin management of rate plans and their immutable versions.</summary>
[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
[Route("api/tenant/rate-plans")]
public sealed class RatePlansController : ApiControllerBase
{
    private readonly IRatePlanService _ratePlans;

    public RatePlansController(IRatePlanService ratePlans) => _ratePlans = ratePlans;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? locationId, [FromQuery] PageQuery page, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<RatePlanResponse>>.Ok(await _ratePlans.ListAsync(locationId, page, ct)));

    [HttpGet("{id:guid}", Name = "RatePlansGet")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<RatePlanResponse>.Ok(await _ratePlans.GetAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRatePlanRequest request, CancellationToken ct)
    {
        var created = await _ratePlans.CreateAsync(request, ct);
        return CreatedAtRoute("RatePlansGet", new { id = created.Id }, ApiResponse<RatePlanResponse>.Ok(created));
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<IActionResult> ListVersions(Guid id, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<RatePlanVersionResponse>>.Ok(await _ratePlans.ListVersionsAsync(id, ct)));

    [HttpPost("{id:guid}/versions")]
    public async Task<IActionResult> AddVersion(Guid id, [FromBody] AddRatePlanVersionRequest request, CancellationToken ct)
        => Ok(ApiResponse<RatePlanVersionResponse>.Ok(await _ratePlans.AddVersionAsync(id, request, ct)));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await _ratePlans.ArchiveAsync(id, ct);
        return NoContent();
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Benefits;
using ParkingSaaS.Contracts.Benefits;
using ParkingSaaS.Contracts.Common;

namespace ParkingSaaS.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
[Route("api/tenant/corporate-benefits")]
public sealed class CorporateBenefitsController : ApiControllerBase
{
    private readonly ICorporateBenefitService _benefits;

    public CorporateBenefitsController(ICorporateBenefitService benefits) => _benefits = benefits;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<CorporateBenefitProgramResponse>>.Ok(await _benefits.ListAsync(ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<CorporateBenefitProgramResponse>.Ok(await _benefits.GetAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCorporateBenefitRequest request, CancellationToken ct)
    {
        var created = await _benefits.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse<CorporateBenefitProgramResponse>.Ok(created));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCorporateBenefitRequest request, CancellationToken ct)
        => Ok(ApiResponse<CorporateBenefitProgramResponse>.Ok(await _benefits.UpdateAsync(id, request, ct)));

    [HttpPost("{id:guid}/status")]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetCorporateBenefitStatusRequest request, CancellationToken ct)
    {
        await _benefits.SetStatusAsync(id, request.Status, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/allocations")]
    public async Task<IActionResult> Allocations(Guid id, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<CorporateBenefitAllocationResponse>>.Ok(await _benefits.ListAllocationsAsync(id, ct)));

    [HttpGet("{id:guid}/availability/{locationId:guid}")]
    public async Task<IActionResult> Availability(Guid id, Guid locationId, CancellationToken ct)
        => Ok(ApiResponse<CorporateBenefitAvailabilityResponse>.Ok(await _benefits.GetAvailabilityAsync(id, locationId, ct)));
}

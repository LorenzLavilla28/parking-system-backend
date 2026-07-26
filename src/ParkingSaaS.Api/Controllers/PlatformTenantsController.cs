using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Tenants;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Tenants;

namespace ParkingSaaS.Api.Controllers;

/// <summary>Platform-administrator management of tenants.</summary>
[Authorize(Policy = AuthorizationPolicies.PlatformAdmin)]
[Route("api/platform/tenants")]
public sealed class PlatformTenantsController : ApiControllerBase
{
    private readonly ITenantProvisioningService _tenants;

    public PlatformTenantsController(ITenantProvisioningService tenants) => _tenants = tenants;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] PageQuery query, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<TenantResponse>>.Ok(await _tenants.ListAsync(query, ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<TenantResponse>.Ok(await _tenants.GetAsync(id, ct)));

    [HttpGet("{id:guid}/audit-history")]
    public async Task<IActionResult> AuditHistory(Guid id, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<TenantAuditLogResponse>>.Ok(await _tenants.GetAuditHistoryAsync(id, ct)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request, CancellationToken ct)
    {
        var created = await _tenants.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, ApiResponse<TenantResponse>.Ok(created));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] UpdateTenantStatusRequest request, CancellationToken ct)
        => Ok(ApiResponse<TenantResponse>.Ok(await _tenants.ChangeStatusAsync(id, request, ct)));

    [HttpPatch("{id:guid}/plan")]
    public async Task<IActionResult> ChangePlan(Guid id, [FromBody] UpdateTenantPlanRequest request, CancellationToken ct)
        => Ok(ApiResponse<TenantResponse>.Ok(await _tenants.ChangePlanAsync(id, request, ct)));

    [HttpPatch("{id:guid}/capacity-addon")]
    public async Task<IActionResult> UpdateCapacityAddon(Guid id, [FromBody] UpdateTenantCapacityAddonRequest request, CancellationToken ct)
        => Ok(ApiResponse<TenantResponse>.Ok(await _tenants.UpdateCapacityAddonAsync(id, request, ct)));
}

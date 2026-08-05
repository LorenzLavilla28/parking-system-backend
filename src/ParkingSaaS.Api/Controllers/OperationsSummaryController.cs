using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Reports;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Reports;

namespace ParkingSaaS.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
[Route("api/tenant/reports/operations-summary")]
public sealed class OperationsSummaryController : ApiControllerBase
{
    private readonly IOperationsSummaryService _summaries;

    public OperationsSummaryController(IOperationsSummaryService summaries) => _summaries = summaries;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int hours = 3, CancellationToken ct = default)
        => Ok(ApiResponse<OperationsSummaryResponse>.Ok(await _summaries.GetCurrentAsync(hours, ct)));

    [HttpPost("email")]
    public async Task<IActionResult> SendEmail([FromQuery] int hours = 3, CancellationToken ct = default)
        => Ok(ApiResponse<OperationsSummaryEmailResponse>.Ok(await _summaries.QueueCurrentEmailAsync(hours, ct)));

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
        => Ok(ApiResponse<OperationsSummarySettingsResponse>.Ok(await _summaries.GetSettingsAsync(ct)));

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateOperationsSummarySettingsRequest request,
        CancellationToken ct)
        => Ok(ApiResponse<OperationsSummarySettingsResponse>.Ok(await _summaries.UpdateSettingsAsync(request, ct)));
}

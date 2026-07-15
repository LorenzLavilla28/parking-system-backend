using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Supervisor;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Supervisor;

namespace ParkingSaaS.Api.Controllers;

/// <summary>
/// Supervisor/tenant-admin manual overrides. Every action requires a reason and
/// produces an audit log entry with old/new values.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.SupervisorOrAbove)]
[Route("api/supervisor/overrides")]
public sealed class SupervisorOverridesController : ApiControllerBase
{
    private readonly ISupervisorOverrideService _overrides;

    public SupervisorOverridesController(ISupervisorOverrideService overrides) => _overrides = overrides;

    [HttpPost("void")]
    public async Task<IActionResult> Void([FromBody] VoidSessionRequest request, CancellationToken ct)
        => Ok(ApiResponse<OverrideResponse>.Ok(await _overrides.VoidAsync(request, ClientIp, ct)));

    [HttpPost("complimentary")]
    public async Task<IActionResult> Complimentary([FromBody] ComplimentaryRequest request, CancellationToken ct)
        => Ok(ApiResponse<OverrideResponse>.Ok(await _overrides.MarkComplimentaryAsync(request, ClientIp, ct)));

    [HttpPost("waive")]
    public async Task<IActionResult> Waive([FromBody] WaiveOutstandingRequest request, CancellationToken ct)
        => Ok(ApiResponse<OverrideResponse>.Ok(await _overrides.WaiveOutstandingAsync(request, ClientIp, ct)));

    [HttpPost("correct-entry-time")]
    public async Task<IActionResult> CorrectEntryTime([FromBody] CorrectEntryTimeRequest request, CancellationToken ct)
        => Ok(ApiResponse<OverrideResponse>.Ok(await _overrides.CorrectEntryTimeAsync(request, ClientIp, ct)));

    [HttpPost("correct-plate")]
    public async Task<IActionResult> CorrectPlate([FromBody] CorrectPlateRequest request, CancellationToken ct)
        => Ok(ApiResponse<OverrideResponse>.Ok(await _overrides.CorrectPlateAsync(request, ClientIp, ct)));
}

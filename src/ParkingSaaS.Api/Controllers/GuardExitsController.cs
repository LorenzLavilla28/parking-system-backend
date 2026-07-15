using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Api.Controllers;

/// <summary>Guard exit validation: status banner and server-revalidated approval.</summary>
[Authorize(Policy = AuthorizationPolicies.GuardOrAbove)]
[Route("api/guard/exits")]
public sealed class GuardExitsController : ApiControllerBase
{
    private readonly IGuardExitService _exits;

    public GuardExitsController(IGuardExitService exits) => _exits = exits;

    [HttpGet("{sessionId:guid}")]
    public async Task<IActionResult> Status(Guid sessionId, CancellationToken ct)
        => Ok(ApiResponse<ExitStatusResponse>.Ok(await _exits.GetExitStatusAsync(sessionId, ct)));

    [HttpPost]
    public async Task<IActionResult> Approve([FromBody] ApproveExitRequest request, CancellationToken ct)
        => Ok(ApiResponse<ExitApprovedResponse>.Ok(await _exits.ApproveExitAsync(request, ClientIp, ct)));
}

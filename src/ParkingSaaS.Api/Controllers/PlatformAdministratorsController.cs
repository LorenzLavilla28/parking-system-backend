using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Platform;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Platform;

namespace ParkingSaaS.Api.Controllers;

/// <summary>Platform-administrator management of platform administrators.</summary>
[Authorize(Policy = AuthorizationPolicies.PlatformAdmin)]
[Route("api/platform/administrators")]
public sealed class PlatformAdministratorsController : ApiControllerBase
{
    private readonly IPlatformAdministratorService _administrators;

    public PlatformAdministratorsController(IPlatformAdministratorService administrators)
        => _administrators = administrators;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<PlatformAdministratorResponse>>.Ok(await _administrators.ListAsync(ct)));

    [HttpPost]
    public async Task<IActionResult> Invite(
        [FromBody] InvitePlatformAdministratorRequest request, CancellationToken ct)
        => Ok(ApiResponse<InvitePlatformAdministratorResponse>.Ok(await _administrators.InviteAsync(request, ct)));
}

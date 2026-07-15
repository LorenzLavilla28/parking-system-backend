using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Api.Controllers;

/// <summary>Locations the signed-in guard/supervisor may operate.</summary>
[Authorize(Policy = AuthorizationPolicies.GuardOrAbove)]
[Route("api/guard/locations")]
public sealed class GuardLocationsController : ApiControllerBase
{
    private readonly IGuardLocationService _locations;

    public GuardLocationsController(IGuardLocationService locations) => _locations = locations;

    [HttpGet]
    public async Task<IActionResult> Mine(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<GuardLocationResponse>>.Ok(await _locations.GetMyLocationsAsync(ct)));
}

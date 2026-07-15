using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Api.Controllers;

/// <summary>Guard-facing session search, detail, and QR reprint.</summary>
[Authorize(Policy = AuthorizationPolicies.GuardOrAbove)]
[Route("api/guard/sessions")]
public sealed class GuardSessionsController : ApiControllerBase
{
    private readonly IGuardSessionService _sessions;

    public GuardSessionsController(IGuardSessionService sessions) => _sessions = sessions;

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? plate,
        [FromQuery] string? status,
        [FromQuery] Guid? locationId,
        [FromQuery] bool activeOnly,
        [FromQuery] PageQuery page,
        CancellationToken ct)
    {
        var result = await _sessions.SearchAsync(plate, status, locationId, activeOnly, page, ct);
        return Ok(ApiResponse<PagedResult<SessionSummaryResponse>>.Ok(result));
    }

    [HttpGet("{id:guid}", Name = "GuardSessionsGet")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SessionSummaryResponse>.Ok(await _sessions.GetAsync(id, ct)));

    [HttpPost("{id:guid}/qr")]
    public async Task<IActionResult> Qr(Guid id, CancellationToken ct)
        => Ok(ApiResponse<SessionQrResponse>.Ok(await _sessions.GetQrAsync(id, ct)));
}

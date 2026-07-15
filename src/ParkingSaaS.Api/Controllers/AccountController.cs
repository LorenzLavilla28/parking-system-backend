using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Application.Auth;
using ParkingSaaS.Contracts.Auth;
using ParkingSaaS.Contracts.Common;

namespace ParkingSaaS.Api.Controllers;

[Authorize]
[Route("api/account")]
public sealed class AccountController : ApiControllerBase
{
    private readonly IAuthService _auth;

    public AccountController(IAuthService auth) => _auth = auth;

    [HttpPost("change-password")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var result = await _auth.ChangePasswordAsync(request, userId, ClientIp, ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }
}

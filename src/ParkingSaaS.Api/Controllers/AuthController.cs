using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ParkingSaaS.Application.Auth;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Contracts.Auth;
using ParkingSaaS.Contracts.Common;

namespace ParkingSaaS.Api.Controllers;

[AllowAnonymous]
[EnableRateLimiting("auth")]
[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;
    private readonly EmailOptions _emailOptions;

    public AuthController(IAuthService auth, Microsoft.Extensions.Options.IOptions<EmailOptions> emailOptions)
    {
        _auth = auth;
        _emailOptions = emailOptions.Value;
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request, ClientIp, ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var result = await _auth.RefreshAsync(request, ClientIp, ct);
        return Ok(ApiResponse<AuthResponse>.Ok(result));
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        await _auth.LogoutAsync(request, ct);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(ApiResponse<PasswordResetResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var result = await _auth.RequestPasswordResetAsync(request, _emailOptions.AppBaseUrl, ct);
        return Ok(ApiResponse<PasswordResetResponse>.Ok(result));
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(ApiResponse<PasswordResetResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        await _auth.ResetPasswordAsync(request, ct);
        return Ok(ApiResponse<PasswordResetResponse>.Ok(
            new PasswordResetResponse("Your password has been reset. You can now sign in.")));
    }
}

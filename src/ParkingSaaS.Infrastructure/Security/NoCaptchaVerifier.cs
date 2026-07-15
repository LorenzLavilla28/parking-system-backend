using Microsoft.Extensions.Logging;
using ParkingSaaS.Application.Abstractions;

namespace ParkingSaaS.Infrastructure.Security;

/// <summary>
/// Default CAPTCHA verifier for development: accepts any non-empty token and
/// logs that real verification is not configured. Swap this registration for a
/// provider-backed verifier (reCAPTCHA/hCaptcha/Turnstile) in production.
/// </summary>
public sealed class NoCaptchaVerifier : ICaptchaVerifier
{
    private readonly ILogger<NoCaptchaVerifier> _logger;

    public NoCaptchaVerifier(ILogger<NoCaptchaVerifier> logger) => _logger = logger;

    public Task<bool> VerifyAsync(string? captchaToken, string? remoteIp, CancellationToken ct)
    {
        _logger.LogDebug("CAPTCHA verification stubbed; configure a real provider for production.");
        return Task.FromResult(!string.IsNullOrWhiteSpace(captchaToken));
    }
}

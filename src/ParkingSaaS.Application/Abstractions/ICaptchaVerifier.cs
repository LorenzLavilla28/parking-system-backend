namespace ParkingSaaS.Application.Abstractions;

/// <summary>
/// Verifies a CAPTCHA challenge response. Abstracted so the provider
/// (reCAPTCHA, hCaptcha, Turnstile) can be swapped without touching use cases.
/// </summary>
public interface ICaptchaVerifier
{
    Task<bool> VerifyAsync(string? captchaToken, string? remoteIp, CancellationToken ct);
}

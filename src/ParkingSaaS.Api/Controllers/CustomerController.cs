using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ParkingSaaS.Application.Customer;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Application.Tenants;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Customer;

namespace ParkingSaaS.Api.Controllers;

/// <summary>
/// Public, unauthenticated customer endpoints: location info, lost-ticket plate
/// lookup (rate limited), the masked public session page, fee quotes, and online
/// payment checkout + status polling.
/// </summary>
[AllowAnonymous]
[Route("api/customer")]
public sealed class CustomerController : ApiControllerBase
{
    private readonly ICustomerPublicService _service;
    private readonly ICustomerPricingService _pricing;
    private readonly ICustomerPaymentService _payments;
    private readonly ITenantBrandingService _branding;

    public CustomerController(
        ICustomerPublicService service,
        ICustomerPricingService pricing,
        ICustomerPaymentService payments,
        ITenantBrandingService branding)
    {
        _service = service;
        _pricing = pricing;
        _payments = payments;
        _branding = branding;
    }

    [HttpGet("locations/{slug}")]
    public async Task<IActionResult> GetLocation(string slug, CancellationToken ct)
        => Ok(ApiResponse<PublicLocationResponse>.Ok(await _service.GetLocationAsync(slug, ct)));

    [HttpGet("locations/{slug}/logo")]
    public async Task<IActionResult> GetLocationLogo(string slug, CancellationToken ct)
    {
        var logo = await _branding.DownloadLogoForLocationAsync(slug, ct);
        return File(logo.Content, logo.ContentType, enableRangeProcessing: false);
    }

    [HttpPost("locations/{slug}/lookup")]
    [EnableRateLimiting("public-lookup")]
    public async Task<IActionResult> Lookup(string slug, [FromBody] PlateLookupRequest request, CancellationToken ct)
    {
        // Throttle per location + client IP so one noisy client cannot brute-force plates.
        var ip = ClientIp ?? "unknown";
        var client = new PublicClientContext($"{slug}:{ip}", ip);
        var result = await _service.LookupByPlateAsync(slug, request, client, ct);
        return Ok(ApiResponse<PlateLookupResponse>.Ok(result));
    }

    [HttpGet("sessions/{publicToken}")]
    public async Task<IActionResult> GetSession(string publicToken, CancellationToken ct)
        => Ok(ApiResponse<PublicSessionResponse>.Ok(await _service.GetSessionByTokenAsync(publicToken, ct)));

    [HttpGet("sessions/{publicToken}/fee")]
    public async Task<IActionResult> GetFee(string publicToken, CancellationToken ct)
        => Ok(ApiResponse<CurrentFeeResponse>.Ok(await _pricing.GetCurrentFeeAsync(publicToken, ct)));

    [HttpPost("fee-quotes")]
    public async Task<IActionResult> CreateQuote([FromBody] CreateFeeQuoteRequest request, CancellationToken ct)
        => Ok(ApiResponse<FeeQuoteResponse>.Ok(await _pricing.CreateQuoteAsync(request, ct)));

    [HttpPost("payments")]
    public async Task<IActionResult> CreateCheckout([FromBody] StartCheckoutRequest request, CancellationToken ct)
        => Ok(ApiResponse<CheckoutResponse>.Ok(await _payments.CreateCheckoutAsync(
            request, ClientIp, Request.Headers.UserAgent.ToString(), ct)));

    [HttpGet("payments/{reference}/status")]
    public async Task<IActionResult> PaymentStatus(string reference, CancellationToken ct)
        => Ok(ApiResponse<PaymentStatusResponse>.Ok(await _payments.GetStatusAsync(reference, ct)));
}

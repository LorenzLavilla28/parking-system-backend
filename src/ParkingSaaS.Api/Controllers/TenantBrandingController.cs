using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Tenants;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Tenants;

namespace ParkingSaaS.Api.Controllers;

[Route("api/tenant/branding")]
public sealed class TenantBrandingController : ApiControllerBase
{
    private const long RequestLimitBytes = 2 * 1024 * 1024 + 64 * 1024;
    private readonly ITenantBrandingService _branding;

    public TenantBrandingController(ITenantBrandingService branding) => _branding = branding;

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(ApiResponse<TenantBrandingResponse>.Ok(await _branding.GetAsync(ct)));

    [HttpPost("logo")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
    [RequestSizeLimit(RequestLimitBytes)]
    public async Task<IActionResult> UploadLogo([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null)
            throw new ConflictException("Choose a logo file to upload.");

        await using var content = new MemoryStream();
        await file.CopyToAsync(content, ct);
        content.Position = 0;
        var result = await _branding.UploadLogoAsync(content, file.ContentType, ct);
        return Ok(ApiResponse<TenantBrandingResponse>.Ok(result));
    }

    [HttpGet("logo")]
    [Authorize(Policy = AuthorizationPolicies.GuardOrAbove)]
    public async Task<IActionResult> DownloadLogo(CancellationToken ct)
    {
        var logo = await _branding.DownloadCurrentLogoAsync(ct);
        if (logo is null)
            return NoContent();

        return File(logo.Content, logo.ContentType, enableRangeProcessing: false);
    }

    [HttpDelete("logo")]
    [Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
    public async Task<IActionResult> DeleteLogo(CancellationToken ct)
    {
        await _branding.RemoveLogoAsync(ct);
        return NoContent();
    }
}

using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Controllers;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Tenants;
using ParkingSaaS.Contracts.Tenants;

namespace ParkingSaaS.UnitTests.Tenants;

public sealed class TenantBrandingControllerTests
{
    [Fact]
    public async Task DownloadLogo_returns_no_content_when_tenant_has_no_logo()
    {
        var controller = new TenantBrandingController(new NoLogoBrandingService());

        var result = await controller.DownloadLogo(CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
    }

    private sealed class NoLogoBrandingService : ITenantBrandingService
    {
        public Task<TenantBrandingResponse> GetAsync(CancellationToken ct)
            => throw new NotSupportedException();

        public Task<TenantBrandingResponse> UploadLogoAsync(
            Stream content,
            string contentType,
            CancellationToken ct)
            => throw new NotSupportedException();

        public Task RemoveLogoAsync(CancellationToken ct)
            => throw new NotSupportedException();

        public Task<TenantLogoDownload?> DownloadCurrentLogoAsync(CancellationToken ct)
            => Task.FromResult<TenantLogoDownload?>(null);

        public Task<TenantLogoDownload> DownloadLogoForLocationAsync(string locationSlug, CancellationToken ct)
            => throw new NotSupportedException();
    }
}

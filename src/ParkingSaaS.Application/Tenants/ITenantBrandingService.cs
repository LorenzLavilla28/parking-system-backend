using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Contracts.Tenants;

namespace ParkingSaaS.Application.Tenants;

public interface ITenantBrandingService
{
    Task<TenantBrandingResponse> GetAsync(CancellationToken ct);
    Task<TenantBrandingResponse> UploadLogoAsync(Stream content, string contentType, CancellationToken ct);
    Task RemoveLogoAsync(CancellationToken ct);
    Task<TenantLogoDownload?> DownloadCurrentLogoAsync(CancellationToken ct);
    Task<TenantLogoDownload> DownloadLogoForLocationAsync(string locationSlug, CancellationToken ct);
}

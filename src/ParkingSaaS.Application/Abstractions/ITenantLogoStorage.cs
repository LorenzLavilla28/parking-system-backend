namespace ParkingSaaS.Application.Abstractions;

public interface ITenantLogoStorage
{
    Task PutAsync(string objectKey, Stream content, string contentType, long contentLength, CancellationToken ct);
    Task<TenantLogoDownload?> GetAsync(string objectKey, CancellationToken ct);
    Task DeleteAsync(string objectKey, CancellationToken ct);
}

public sealed record TenantLogoDownload(Stream Content, string ContentType, long? ContentLength);

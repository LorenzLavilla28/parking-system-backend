using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;

namespace ParkingSaaS.Infrastructure.TenantAssets;

public sealed class S3TenantLogoStorage : ITenantLogoStorage
{
    private readonly IAmazonS3 _s3;
    private readonly TenantBrandingOptions _options;
    private readonly ILogger<S3TenantLogoStorage> _logger;

    public S3TenantLogoStorage(
        IAmazonS3 s3,
        IOptions<TenantBrandingOptions> options,
        ILogger<S3TenantLogoStorage> logger)
    {
        _s3 = s3;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PutAsync(string objectKey, Stream content, string contentType, long contentLength, CancellationToken ct)
    {
        EnsureBucketConfigured();
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
        }, ct);
    }

    public async Task<TenantLogoDownload?> GetAsync(string objectKey, CancellationToken ct)
    {
        EnsureBucketConfigured();
        try
        {
            var response = await _s3.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
            }, ct);
            return new TenantLogoDownload(response.ResponseStream, response.Headers.ContentType, response.Headers.ContentLength);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Tenant logo object {ObjectKey} was not found in bucket {BucketName}.", objectKey, _options.BucketName);
            return null;
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        EnsureBucketConfigured();
        await _s3.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
        }, ct);
    }

    private void EnsureBucketConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.BucketName))
            throw new InvalidOperationException("TenantBranding:BucketName is not configured.");
    }
}

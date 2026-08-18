using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Contracts.Tenants;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Tenants;

namespace ParkingSaaS.Application.Tenants;

public sealed class TenantBrandingService : ITenantBrandingService
{
    private static readonly IReadOnlyDictionary<string, (string ContentType, string Extension)> AllowedTypes =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"] = ("image/png", "png"),
            ["image/jpeg"] = ("image/jpeg", "jpg"),
            ["image/jpg"] = ("image/jpeg", "jpg"),
            ["image/webp"] = ("image/webp", "webp"),
        };

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ITenantLogoStorage _storage;
    private readonly TenantBrandingOptions _options;
    private readonly ILogger<TenantBrandingService> _logger;

    public TenantBrandingService(
        IApplicationDbContext db,
        ITenantContext tenant,
        ITenantLogoStorage storage,
        IOptions<TenantBrandingOptions> options,
        ILogger<TenantBrandingService> logger)
    {
        _db = db;
        _tenant = tenant;
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TenantBrandingResponse> GetAsync(CancellationToken ct)
    {
        var tenant = await CurrentTenantAsync(ct);
        return ToResponse(tenant);
    }

    public async Task<TenantBrandingResponse> UploadLogoAsync(Stream content, string contentType, CancellationToken ct)
    {
        if (content is null || !content.CanRead)
            throw new ConflictException("A readable logo file is required.");
        if (content.Length <= 0 || content.Length > _options.MaxLogoBytes)
            throw new ConflictException($"Logo files must be between 1 byte and {_options.MaxLogoBytes} bytes.");

        var normalizedType = NormalizeAndValidateType(contentType);
        await ValidateSignatureAsync(content, normalizedType.ContentType, ct);

        var tenant = await CurrentTenantAsync(ct);
        var oldKey = tenant.LogoObjectKey;
        var objectKey = $"tenants/{tenant.Id:N}/branding/logo-{Guid.NewGuid():N}.{normalizedType.Extension}";

        try
        {
            if (content.CanSeek)
                content.Position = 0;
            await _storage.PutAsync(objectKey, content, normalizedType.ContentType, content.Length, ct);

            tenant.SetLogo(objectKey, normalizedType.ContentType);
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            await DeleteQuietlyAsync(objectKey, ct);
            throw;
        }

        if (!string.IsNullOrWhiteSpace(oldKey) && !string.Equals(oldKey, objectKey, StringComparison.Ordinal))
            await DeleteQuietlyAsync(oldKey, ct);

        return ToResponse(tenant);
    }

    public async Task RemoveLogoAsync(CancellationToken ct)
    {
        var tenant = await CurrentTenantAsync(ct);
        var oldKey = tenant.LogoObjectKey;
        if (string.IsNullOrWhiteSpace(oldKey))
            return;

        tenant.ClearLogo();
        await _db.SaveChangesAsync(ct);
        await DeleteQuietlyAsync(oldKey, ct);
    }

    public async Task<TenantLogoDownload?> DownloadCurrentLogoAsync(CancellationToken ct)
    {
        var tenant = await CurrentTenantAsync(ct);
        if (string.IsNullOrWhiteSpace(tenant.LogoObjectKey))
            return null;

        return await _storage.GetAsync(tenant.LogoObjectKey, ct);
    }

    public async Task<TenantLogoDownload> DownloadLogoForLocationAsync(string locationSlug, CancellationToken ct)
    {
        var normalizedSlug = (locationSlug ?? string.Empty).Trim().ToLowerInvariant();
        var location = await _db.ParkingLocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Slug == normalizedSlug && l.Status == LocationStatus.Active, ct)
            ?? throw new NotFoundException("Parking location not found.");
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == location.TenantId && t.Status == TenantStatus.Active, ct)
            ?? throw new NotFoundException("Tenant not found.");

        return await DownloadAsync(tenant.LogoObjectKey, tenant.LogoContentType, ct);
    }

    private async Task<TenantLogoDownload> DownloadAsync(string? objectKey, string? contentType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
            throw new NotFoundException("This tenant has no logo configured.");

        return await _storage.GetAsync(objectKey, ct)
            ?? throw new NotFoundException("The configured tenant logo could not be found.");
    }

    private async Task<Domain.Tenants.Tenant> CurrentTenantAsync(CancellationToken ct)
    {
        if (!_tenant.HasTenant)
            throw new ConflictException("A tenant context is required.");

        return await _db.Tenants.FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct)
            ?? throw new NotFoundException("Tenant not found.");
    }

    private TenantBrandingResponse ToResponse(Domain.Tenants.Tenant tenant)
        => new(
            tenant.LogoObjectKey is null ? null : "/api/tenant/branding/logo",
            tenant.LogoContentType,
            _options.MaxLogoBytes);

    private static (string ContentType, string Extension) NormalizeAndValidateType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) || !AllowedTypes.TryGetValue(contentType.Trim(), out var type))
            throw new ConflictException("Only PNG, JPEG, and WebP logo files are supported.");
        return type;
    }

    private static async Task ValidateSignatureAsync(Stream content, string contentType, CancellationToken ct)
    {
        if (!content.CanSeek)
            throw new ConflictException("The logo stream must support validation.");

        var header = new byte[12];
        content.Position = 0;
        var read = 0;
        while (read < header.Length)
        {
            var chunk = await content.ReadAsync(header.AsMemory(read, header.Length - read), ct);
            if (chunk == 0) break;
            read += chunk;
        }
        content.Position = 0;

        var valid = contentType switch
        {
            "image/png" => read >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            "image/jpeg" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "image/webp" => read >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false,
        };

        if (!valid)
            throw new ConflictException("The uploaded file does not match its image type.");
    }

    private async Task DeleteQuietlyAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await _storage.DeleteAsync(objectKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to delete tenant logo object {ObjectKey}.", objectKey);
        }
    }
}

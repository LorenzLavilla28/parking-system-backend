using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Domain.Payments;

namespace ParkingSaaS.Application.Payments;

public sealed class PayMongoCredentialsResolver : IPayMongoCredentialsResolver
{
    private readonly IApplicationDbContext _db;
    private readonly IPayMongoCredentialStore _store;
    private readonly IMemoryCache _cache;
    private readonly PayMongoOptions _options;

    public PayMongoCredentialsResolver(
        IApplicationDbContext db,
        IPayMongoCredentialStore store,
        IMemoryCache cache,
        IOptions<PayMongoOptions> options)
    {
        _db = db;
        _store = store;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<ResolvedPayMongoCredentials?> ResolveAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId is { } id)
        {
            var connection = await _db.TenantPayMongoConnections
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.TenantId == id
                            && c.Environment == TenantPayMongoConnection.NormalizeEnvironment(_options.ActiveEnvironment)
                            && c.Status == PayMongoConnectionStatus.Connected)
                .FirstOrDefaultAsync(cancellationToken);

            if (connection is not null)
            {
                var cacheKey = CacheKey(connection.TenantId, connection.Environment);
                if (_cache.TryGetValue<PayMongoSecretValues>(cacheKey, out var cached) && cached is not null)
                    return new(cached.SecretKey, cached.WebhookSecret, false, connection.PayMongoAccountId);

                var values = await _store.GetAsync(connection.SecretArn, cancellationToken);
                if (values is not null)
                {
                    _cache.Set(cacheKey, values, TimeSpan.FromMinutes(Math.Max(1, _options.CredentialCacheMinutes)));
                    return new(values.SecretKey, values.WebhookSecret, false, connection.PayMongoAccountId);
                }
            }
        }

        // PayMongo credentials are tenant-owned. There is intentionally no
        // application/local fallback: a tenant without a connected account is
        // cash-only until its own credentials are connected.
        return null;
    }

    public void Invalidate(Guid tenantId, string environment)
        => _cache.Remove(CacheKey(tenantId, TenantPayMongoConnection.NormalizeEnvironment(environment)));

    private static string CacheKey(Guid tenantId, string environment)
        => $"paymongo:{tenantId:N}:{environment}";
}

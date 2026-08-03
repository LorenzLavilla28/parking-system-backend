using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Audit;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Contracts.Payments;
using ParkingSaaS.Domain.Payments;

namespace ParkingSaaS.Application.Payments;

public sealed class PayMongoConnectionService : IPayMongoConnectionService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPayMongoCredentialStore _store;
    private readonly IPayMongoCredentialValidator _validator;
    private readonly IPayMongoCredentialsResolver _credentialsResolver;
    private readonly IParkingTokenService _tokens;
    private readonly IAuditLogger? _audit;
    private readonly PayMongoOptions _options;

    public PayMongoConnectionService(
        IApplicationDbContext db,
        ITenantContext tenant,
        IPayMongoCredentialStore store,
        IPayMongoCredentialValidator validator,
        IPayMongoCredentialsResolver credentialsResolver,
        IParkingTokenService tokens,
        IOptions<PayMongoOptions> options,
        IAuditLogger? audit = null)
    {
        _db = db;
        _tenant = tenant;
        _store = store;
        _validator = validator;
        _credentialsResolver = credentialsResolver;
        _tokens = tokens;
        _options = options.Value;
        _audit = audit;
    }

    public async Task<PayMongoConnectionResponse?> GetAsync(CancellationToken cancellationToken)
    {
        EnsureTenant();
        var connection = await _db.TenantPayMongoConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        return connection is null ? null : ToResponse(connection);
    }

    public async Task<PayMongoConnectionResponse> ConnectAsync(
        ConnectPayMongoRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenant();
        var secretKey = request.SecretKey.Trim();
        var webhookSecret = request.WebhookSecret.Trim();

        if (string.IsNullOrWhiteSpace(webhookSecret))
            throw new ConflictException("A PayMongo webhook secret is required.");

        var validation = await _validator.ValidateAsync(secretKey, cancellationToken);
        if (!validation.IsValid)
            throw new ConflictException(validation.Error ?? "PayMongo credentials could not be validated.");

        var secretName = $"{_options.SecretNamePrefix.TrimEnd('/')}/{_tenant.TenantId}/paymongo/live";
        var secretArn = await _store.CreateOrUpdateAsync(
            secretName,
            new PayMongoSecretValues(secretKey, webhookSecret),
            cancellationToken);

        var connection = await _db.TenantPayMongoConnections
            .FirstOrDefaultAsync(cancellationToken);

        if (connection is null)
        {
            var webhookToken = _tokens.GeneratePublicToken();
            connection = new TenantPayMongoConnection(
                _tenant.TenantId,
                secretArn,
                _tokens.Hash(webhookToken),
                _tokens.Protect(webhookToken));
            await _db.TenantPayMongoConnections.AddAsync(connection, cancellationToken);
        }

        connection.MarkConnected(request.PayMongoAccountId ?? validation.AccountId, DateTimeOffset.UtcNow);

        if (_audit is not null)
        {
            await _audit.AddAsync(
                _tenant.TenantId,
                null,
                "PayMongoConnected",
                nameof(TenantPayMongoConnection),
                connection.Id.ToString(),
                oldValues: null,
                newValues: new { Mode = "live", connection.Status, connection.PayMongoAccountId },
                reason: null,
                new AuditContext(null, null),
                cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        _credentialsResolver.Invalidate(_tenant.TenantId);
        return ToResponse(connection);
    }

    public async Task<PayMongoConnectionResponse> DisconnectAsync(CancellationToken cancellationToken)
    {
        EnsureTenant();
        var connection = await _db.TenantPayMongoConnections
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("PayMongo is not connected for this tenant.");

        connection.MarkDisconnected(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        _credentialsResolver.Invalidate(_tenant.TenantId);
        return ToResponse(connection);
    }

    private PayMongoConnectionResponse ToResponse(TenantPayMongoConnection connection)
        => new(
            connection.Status.ToString(),
            connection.PayMongoAccountId,
            connection.LastValidatedAt,
            connection.LastError,
            BuildWebhookUrl(connection));

    private string? BuildWebhookUrl(TenantPayMongoConnection connection)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookBaseUrl))
            return null;

        var token = _tokens.Unprotect(connection.WebhookTokenProtected);
        return $"{_options.WebhookBaseUrl.TrimEnd('/')}/api/payments/webhooks/paymongo/{Uri.EscapeDataString(token)}";
    }

    private void EnsureTenant()
    {
        if (!_tenant.HasTenant || _tenant.TenantId == Guid.Empty)
            throw new ForbiddenException("A tenant context is required for PayMongo settings.");
    }
}

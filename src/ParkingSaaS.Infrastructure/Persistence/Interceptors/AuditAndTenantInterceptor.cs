using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Infrastructure.Persistence.Interceptors;

/// <summary>
/// On every save: stamps audit timestamps, fills <see cref="ITenantOwned.TenantId"/>
/// from the ambient tenant when an entity is created without one, and rejects any
/// attempt to write a row belonging to a different tenant. Platform administrators
/// may set an explicit TenantId (e.g. provisioning a new tenant's first admin).
/// </summary>
public sealed class AuditAndTenantInterceptor : SaveChangesInterceptor
{
    private readonly ITenantContext _tenant;
    private readonly IDateTime _clock;
    private readonly ILogger<AuditAndTenantInterceptor> _logger;

    public AuditAndTenantInterceptor(ITenantContext tenant, IDateTime clock, ILogger<AuditAndTenantInterceptor> logger)
    {
        _tenant = tenant;
        _clock = clock;
        _logger = logger;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;
        var now = _clock.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditableEntity auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.CreatedAt = now;
                    auditable.UpdatedAt = now;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.UpdatedAt = now;
                }
            }

            if (entry.Entity is ITenantOwned owned &&
                entry.State is EntityState.Added or EntityState.Modified)
            {
                EnforceTenant(entry, owned);
            }
        }
    }

    private void EnforceTenant(EntityEntry entry, ITenantOwned owned)
    {
        // Platform admins (and the seeder/system context) may write across tenants,
        // and may create legitimately tenant-less rows such as the platform admin
        // user. These paths are audited at the use-case layer.
        if (_tenant.IsPlatformAdministrator) return;

        var prop = entry.Property(nameof(ITenantOwned.TenantId));

        if (entry.State == EntityState.Added && owned.TenantId == Guid.Empty)
        {
            if (!_tenant.HasTenant)
                throw new InvalidOperationException(
                    $"Cannot persist {entry.Entity.GetType().Name}: no tenant in context.");
            prop.CurrentValue = _tenant.TenantId;
            return;
        }

        if (_tenant.HasTenant && owned.TenantId != _tenant.TenantId)
        {
            _logger.LogWarning(
                "Blocked cross-tenant write on {Entity}. Context tenant {ContextTenant}, entity tenant {EntityTenant}.",
                entry.Entity.GetType().Name, _tenant.TenantId, owned.TenantId);
            throw new InvalidOperationException("Cross-tenant write rejected.");
        }
    }
}

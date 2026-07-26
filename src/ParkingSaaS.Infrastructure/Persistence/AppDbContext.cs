using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Domain.Audit;
using ParkingSaaS.Domain.Emails;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.RatePlans;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Tenants;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Infrastructure.Persistence;

/// <summary>
/// EF Core context. Applies a global query filter on every tenant-owned entity
/// keyed to the ambient <see cref="ITenantContext"/>, so isolation is enforced
/// at the data layer and cannot be bypassed by forgetting a WHERE clause.
/// Platform administrators see across tenants via the same filter.
/// </summary>
public sealed class AppDbContext : DbContext, IApplicationDbContext
{
    private readonly ITenantContext _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : base(options)
        => _tenant = tenant;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserParkingLocation> UserParkingLocations => Set<UserParkingLocation>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<ParkingLocation> ParkingLocations => Set<ParkingLocation>();
    public DbSet<ParkingSession> ParkingSessions => Set<ParkingSession>();
    public DbSet<RatePlan> RatePlans => Set<RatePlan>();
    public DbSet<RatePlanVersion> RatePlanVersions => Set<RatePlanVersion>();
    public DbSet<FeeQuote> FeeQuotes => Set<FeeQuote>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<TenantPayMongoConnection> TenantPayMongoConnections => Set<TenantPayMongoConnection>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<EmailMessage> Emails => Set<EmailMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Global tenant filters. The lambdas capture this context instance; EF
        // re-reads the tenant values as query parameters at execution time.
        modelBuilder.Entity<ApplicationUser>()
            .HasQueryFilter(u => _tenant.IsPlatformAdministrator || u.TenantId == _tenant.TenantId);
        modelBuilder.Entity<PasswordResetToken>()
            .HasQueryFilter(t => _tenant.IsPlatformAdministrator || t.TenantId == _tenant.TenantId);
        modelBuilder.Entity<UserParkingLocation>()
            .HasQueryFilter(a => _tenant.IsPlatformAdministrator || a.TenantId == _tenant.TenantId);
        modelBuilder.Entity<ParkingLocation>()
            .HasQueryFilter(l => _tenant.IsPlatformAdministrator || l.TenantId == _tenant.TenantId);
        modelBuilder.Entity<ParkingSession>()
            .HasQueryFilter(s => _tenant.IsPlatformAdministrator || s.TenantId == _tenant.TenantId);
        modelBuilder.Entity<RatePlan>()
            .HasQueryFilter(p => _tenant.IsPlatformAdministrator || p.TenantId == _tenant.TenantId);
        modelBuilder.Entity<RatePlanVersion>()
            .HasQueryFilter(v => _tenant.IsPlatformAdministrator || v.TenantId == _tenant.TenantId);
        modelBuilder.Entity<FeeQuote>()
            .HasQueryFilter(q => _tenant.IsPlatformAdministrator || q.TenantId == _tenant.TenantId);
        modelBuilder.Entity<Payment>()
            .HasQueryFilter(p => _tenant.IsPlatformAdministrator || p.TenantId == _tenant.TenantId);
        modelBuilder.Entity<TenantPayMongoConnection>()
            .HasQueryFilter(c => _tenant.IsPlatformAdministrator || c.TenantId == _tenant.TenantId);
        modelBuilder.Entity<AuditLog>()
            .HasQueryFilter(a => _tenant.IsPlatformAdministrator || a.TenantId == _tenant.TenantId);
        // WebhookEvent is provider-global (not tenant-owned) and is intentionally unfiltered.

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges()
        => base.SaveChanges();

    public new Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => base.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        // EF Core's in-memory provider does not support transactions; unit tests
        // still exercise the same operation body without the database lock.
        if (Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true)
        {
            await operation(cancellationToken);
            return;
        }

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task LockLocationAsync(Guid locationId, CancellationToken cancellationToken = default)
    {
        if (Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true)
            return;

        await Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM parking_locations WHERE \"Id\" = {locationId} FOR UPDATE",
            cancellationToken);
    }

    public async Task LockTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (Database.ProviderName?.Contains("InMemory", StringComparison.OrdinalIgnoreCase) == true)
            return;

        await Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM tenants WHERE \"Id\" = {tenantId} FOR UPDATE",
            cancellationToken);
    }
}

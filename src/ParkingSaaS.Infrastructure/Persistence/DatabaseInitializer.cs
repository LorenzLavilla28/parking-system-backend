using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Infrastructure.Persistence.Interceptors;
using ParkingSaaS.Infrastructure.Persistence.Seed;
using ParkingSaaS.Infrastructure.Time;
using ParkingSaaS.Infrastructure.Tenancy;

namespace ParkingSaaS.Infrastructure.Persistence;

/// <summary>
/// Applies migrations and (optionally) seeds development data at startup. Builds
/// a dedicated <see cref="AppDbContext"/> bound to <see cref="SystemTenantContext"/>
/// so initialization can operate across tenants without any HTTP request scope.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        string connectionString,
        bool seedDevelopmentData,
        bool resetData,
        ILoggerFactory loggerFactory,
        IPasswordHasher passwordHasher,
        BootstrapAdminOptions bootstrapAdmin,
        CancellationToken ct = default)
    {
        var tenantContext = new SystemTenantContext();
        IDateTime clock = new SystemDateTime();
        var interceptor = new AuditAndTenantInterceptor(
            tenantContext, clock, loggerFactory.CreateLogger<AuditAndTenantInterceptor>());

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptor)
            .Options;

        await using var db = new AppDbContext(options, tenantContext);

        // Opt-in dev reset: wipe the public schema (all tables + the migration
        // history) so MigrateAsync rebuilds everything from scratch and the seeder
        // repopulates a clean, deterministic data set. We drop the *schema* rather
        // than the database so the app's least-privileged role can do it without
        // the CREATEDB privilege — the database itself is left in place.
        if (resetData)
        {
            var logger = loggerFactory.CreateLogger(typeof(DatabaseInitializer));
            logger.LogWarning(
                "Database:ResetOnStartup is enabled — wiping the public schema. ALL EXISTING DATA WILL BE LOST.");
            await db.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;", ct);
        }

        await db.Database.MigrateAsync(ct);

        if (seedDevelopmentData)
        {
            var seeder = new DevDataSeeder(db, passwordHasher, loggerFactory.CreateLogger<DevDataSeeder>());
            await seeder.SeedAsync(ct);
        }

        if (bootstrapAdmin.Enabled)
        {
            var seeder = new BootstrapAdminSeeder(
                db,
                passwordHasher,
                loggerFactory.CreateLogger<BootstrapAdminSeeder>());
            await seeder.SeedAsync(bootstrapAdmin, ct);
        }
    }
}

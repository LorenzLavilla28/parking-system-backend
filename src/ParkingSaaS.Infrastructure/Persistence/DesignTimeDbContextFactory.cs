using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ParkingSaaS.Infrastructure.Tenancy;

namespace ParkingSaaS.Infrastructure.Persistence;

/// <summary>
/// Used by the EF Core CLI (migrations) at design time. Reads the connection
/// string from the PARKINGSAAS_MIGRATION_CONNECTION environment variable, or
/// falls back to the local development database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PARKINGSAAS_MIGRATION_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=parkingsaas;Username=parking;Password=parking";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options, new SystemTenantContext());
    }
}

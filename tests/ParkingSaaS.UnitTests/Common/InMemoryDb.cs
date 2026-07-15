using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Infrastructure.Persistence;

namespace ParkingSaaS.UnitTests.Common;

internal static class InMemoryDb
{
    public static AppDbContext Create(MutableTenantContext tenant, string? name = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name ?? $"db-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options, tenant);
    }
}

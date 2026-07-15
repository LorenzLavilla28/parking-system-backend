using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.Infrastructure.Persistence.Interceptors;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Persistence;

public sealed class AuditAndTenantInterceptorTests
{
    private static AppDbContext CreateContext(MutableTenantContext tenant, TestClock clock, string dbName)
    {
        var interceptor = new AuditAndTenantInterceptor(tenant, clock, NullLogger<AuditAndTenantInterceptor>.Instance);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(interceptor)
            .Options;
        return new AppDbContext(options, tenant);
    }

    [Fact]
    public async Task Created_and_updated_timestamps_are_stamped()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new MutableTenantContext();
        tenant.ScopeTo(tenantId);
        var clock = new TestClock(new DateTimeOffset(2026, 6, 23, 10, 0, 0, TimeSpan.Zero));

        await using var db = CreateContext(tenant, clock, $"audit-{Guid.NewGuid()}");
        var location = new ParkingLocation(tenantId, "Lot", "lot", "Asia/Manila", null);
        db.ParkingLocations.Add(location);
        await db.SaveChangesAsync();

        location.CreatedAt.Should().Be(clock.UtcNow);
        location.UpdatedAt.Should().Be(clock.UtcNow);

        clock.Advance(TimeSpan.FromHours(1));
        location.Rename("Lot Renamed");
        await db.SaveChangesAsync();

        location.UpdatedAt.Should().Be(clock.UtcNow);
        location.CreatedAt.Should().Be(clock.UtcNow.AddHours(-1));
    }

    [Fact]
    public async Task Writing_a_row_for_a_foreign_tenant_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenant = Guid.NewGuid();
        var tenant = new MutableTenantContext();
        tenant.ScopeTo(tenantId);
        var clock = new TestClock(DateTimeOffset.UtcNow);

        await using var db = CreateContext(tenant, clock, $"audit-{Guid.NewGuid()}");
        db.ParkingLocations.Add(new ParkingLocation(foreignTenant, "Foreign", "foreign", "Asia/Manila", null));

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cross-tenant write rejected*");
    }
}

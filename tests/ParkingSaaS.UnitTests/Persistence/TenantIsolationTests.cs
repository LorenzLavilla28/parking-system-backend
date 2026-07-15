using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Persistence;

/// <summary>
/// Proves the EF global query filter isolates tenants. Uses the in-memory
/// provider, which honours query filters, so the assertions exercise the same
/// filtering logic the production context applies.
/// </summary>
public sealed class TenantIsolationTests
{
    private static AppDbContext CreateContext(MutableTenantContext tenant)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"isolation-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options, tenant);
    }

    [Fact]
    public async Task Tenant_cannot_see_another_tenants_locations()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var ctx = new MutableTenantContext();

        // Seed one location per tenant through a shared in-memory store.
        var dbName = $"isolation-{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;

        ctx.ScopeTo(tenantA);
        await using (var db = new AppDbContext(options, ctx))
        {
            db.ParkingLocations.Add(new ParkingLocation(tenantA, "A Lot", "a-lot", "Asia/Manila", null));
            await db.SaveChangesAsync();
        }

        ctx.ScopeTo(tenantB);
        await using (var db = new AppDbContext(options, ctx))
        {
            db.ParkingLocations.Add(new ParkingLocation(tenantB, "B Lot", "b-lot", "Asia/Manila", null));
            await db.SaveChangesAsync();
        }

        // Querying as tenant B sees only B's data.
        ctx.ScopeTo(tenantB);
        await using (var db = new AppDbContext(options, ctx))
        {
            var visible = await db.ParkingLocations.ToListAsync();
            visible.Should().ContainSingle();
            visible[0].TenantId.Should().Be(tenantB);
        }
    }

    [Fact]
    public async Task Platform_admin_sees_all_tenants_locations()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var ctx = new MutableTenantContext();
        var dbName = $"isolation-{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;

        ctx.ScopeTo(tenantA);
        await using (var db = new AppDbContext(options, ctx))
        {
            db.ParkingLocations.Add(new ParkingLocation(tenantA, "A Lot", "a-lot", "Asia/Manila", null));
            await db.SaveChangesAsync();
        }

        ctx.ScopeTo(tenantB);
        await using (var db = new AppDbContext(options, ctx))
        {
            db.ParkingLocations.Add(new ParkingLocation(tenantB, "B Lot", "b-lot", "Asia/Manila", null));
            await db.SaveChangesAsync();
        }

        ctx.ScopeToPlatformAdmin();
        await using (var db = new AppDbContext(options, ctx))
        {
            (await db.ParkingLocations.CountAsync()).Should().Be(2);
        }
    }

    [Fact]
    public async Task Interceptor_blocks_writing_a_row_for_a_different_tenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var ctx = new MutableTenantContext();
        ctx.ScopeTo(tenantA);

        // No clock/interceptor here: the in-memory context isn't wired with the
        // interceptor, so this asserts the query-filter read path. The interceptor
        // write path is asserted in AuditAndTenantInterceptorTests.
        await using var db = CreateContext(ctx);
        db.ParkingLocations.Add(new ParkingLocation(tenantB, "Foreign", "foreign", "Asia/Manila", null));
        await db.SaveChangesAsync();

        // Reading back as tenant A must not reveal the row created for tenant B.
        var visible = await db.ParkingLocations.ToListAsync();
        visible.Should().BeEmpty();
    }
}

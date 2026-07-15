using FluentAssertions;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Guard;

public sealed class GuardLocationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly MutableTenantContext _tenant = new();
    private readonly AppDbContext _db;
    private ParkingLocation _a = null!;
    private ParkingLocation _b = null!;

    public GuardLocationServiceTests()
    {
        _tenant.ScopeTo(_tenantId);
        _db = InMemoryDb.Create(_tenant);
        _a = new ParkingLocation(_tenantId, "Lot A", "lot-a", "Asia/Manila", null);
        _b = new ParkingLocation(_tenantId, "Lot B", "lot-b", "Asia/Manila", null);
        _db.ParkingLocations.AddRange(_a, _b);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Guard_sees_only_assigned_locations()
    {
        var guardId = Guid.NewGuid();
        _db.UserParkingLocations.Add(new UserParkingLocation(guardId, _a.Id, _tenantId));
        _db.SaveChanges();

        var user = new FakeCurrentUser { TenantId = _tenantId, UserId = guardId, Roles = new[] { RoleType.Guard } };
        var service = new GuardLocationService(_db, user);

        var result = await service.GetMyLocationsAsync(CancellationToken.None);
        result.Should().ContainSingle().Which.Name.Should().Be("Lot A");
    }

    [Fact]
    public async Task Supervisor_sees_all_tenant_locations()
    {
        var user = new FakeCurrentUser { TenantId = _tenantId, UserId = Guid.NewGuid(), Roles = new[] { RoleType.Supervisor } };
        var service = new GuardLocationService(_db, user);

        var result = await service.GetMyLocationsAsync(CancellationToken.None);
        result.Should().HaveCount(2);
    }
}

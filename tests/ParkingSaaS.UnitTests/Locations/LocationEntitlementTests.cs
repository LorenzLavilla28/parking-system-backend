using FluentAssertions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Locations;
using ParkingSaaS.Contracts.Locations;
using ParkingSaaS.Domain.Tenants;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Locations;

public sealed class LocationEntitlementTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly MutableTenantContext _tenant = new();
    private readonly AppDbContext _db;
    private readonly LocationService _service;

    public LocationEntitlementTests()
    {
        _tenant.ScopeTo(_tenantId);
        _db = InMemoryDb.Create(_tenant);
        var tenant = new Tenant("Acme Parking", "acme", SubscriptionPlan.Growth, "PHP", "Asia/Manila");
        _db.Tenants.Add(tenant);
        _db.SaveChanges();
        _tenant.ScopeTo(tenant.Id);
        _service = new LocationService(_db, _tenant);
    }

    [Fact]
    public async Task Growth_allows_two_locations_but_not_a_third()
    {
        await _service.CreateAsync(Request("one", 50), CancellationToken.None);
        await _service.CreateAsync(Request("two", 50), CancellationToken.None);

        var act = () => _service.CreateAsync(Request("three", 50), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*location_limit_reached*");
    }

    [Fact]
    public async Task Growth_rejects_a_location_above_fifty_slots()
    {
        var act = () => _service.CreateAsync(Request("large", 51), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*capacity_not_allowed*");
    }

    [Fact]
    public async Task Growth_can_restore_an_archived_location_when_capacity_is_available()
    {
        var location = await _service.CreateAsync(Request("one", 50), CancellationToken.None);
        await _service.ArchiveAsync(location.Id, CancellationToken.None);

        await _service.RestoreAsync(location.Id, CancellationToken.None);

        (await _service.GetAsync(location.Id, CancellationToken.None)).Status.Should().Be("Active");
    }

    private static CreateLocationRequest Request(string slug, int slots) => new(
        $"Location {slug}", slug, null, "Asia/Manila", true, slots);
}

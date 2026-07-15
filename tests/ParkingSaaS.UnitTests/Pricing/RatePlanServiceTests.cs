using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Pricing;
using ParkingSaaS.Application.RatePlans;
using ParkingSaaS.Contracts.RatePlans;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.RatePlans;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Pricing;

public sealed class RatePlanServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly MutableTenantContext _tenant = new();
    private readonly FakeCurrentUser _user;
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 6, 24, 0, 0, 0, TimeSpan.Zero));
    private readonly AppDbContext _db;
    private readonly RatePlanService _service;
    private readonly ActiveRatePlanResolver _resolver;
    private ParkingLocation _location = null!;

    public RatePlanServiceTests()
    {
        _tenant.ScopeTo(_tenantId);
        _user = new FakeCurrentUser { TenantId = _tenantId, UserId = Guid.NewGuid() };
        _db = InMemoryDb.Create(_tenant);
        _service = new RatePlanService(_db, _user, _clock);
        _resolver = new ActiveRatePlanResolver(_db);

        _location = new ParkingLocation(_tenantId, "Lot", "lot", "Asia/Manila", null);
        _db.ParkingLocations.Add(_location);
        _db.SaveChanges();
    }

    private static string CanonicalRulesJson() => new PricingRules
    {
        Currency = "PHP",
        EntryGraceMinutes = 15,
        DailyMax = 250m,
        Default = new RateBlock { Type = RateType.FirstBlock, FirstHours = 3, FirstAmount = 50m, IncrementAmount = 20m }
    }.Serialize();

    [Fact]
    public async Task Create_activates_plan_and_writes_first_version()
    {
        var response = await _service.CreateAsync(
            new CreateRatePlanRequest("Standard", "Weekday parking rates", CanonicalRulesJson()), CancellationToken.None);

        response.Status.Should().Be(nameof(RatePlanStatus.Active));
        response.CurrentVersionNumber.Should().Be(1);
        response.ParkingLocationId.Should().BeNull();
        response.Description.Should().Be("Weekday parking rates");
        _location.ActiveRatePlanId.Should().BeNull();

        (await _db.RatePlanVersions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Adding_a_version_increments_number_and_supersedes_prior()
    {
        var plan = await _service.CreateAsync(
            new CreateRatePlanRequest("Standard", "Weekday parking rates", CanonicalRulesJson()), CancellationToken.None);

        _clock.Advance(TimeSpan.FromHours(1));
        var v2 = await _service.AddVersionAsync(plan.Id, new AddRatePlanVersionRequest(CanonicalRulesJson()), CancellationToken.None);

        v2.VersionNumber.Should().Be(2);

        var versions = await _db.RatePlanVersions.IgnoreQueryFilters().OrderBy(v => v.VersionNumber).ToListAsync();
        versions[0].EffectiveTo.Should().NotBeNull("the first version is closed when superseded");
        versions[1].EffectiveTo.Should().BeNull("the newest version is open");
    }

    [Fact]
    public async Task Invalid_rules_are_rejected()
    {
        var act = async () => await _service.CreateAsync(
            new CreateRatePlanRequest("Bad", "Invalid rules", "{ not valid json"), CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Resolver_returns_the_latest_effective_version()
    {
        var plan = await _service.CreateAsync(
            new CreateRatePlanRequest("Standard", "Weekday parking rates", CanonicalRulesJson()), CancellationToken.None);
        _location.AssignRatePlan(plan.Id);
        await _db.SaveChangesAsync();
        _clock.Advance(TimeSpan.FromHours(1));
        var v2 = await _service.AddVersionAsync(plan.Id, new AddRatePlanVersionRequest(CanonicalRulesJson()), CancellationToken.None);

        var resolved = await _resolver.ResolveActiveVersionIdAsync(_location.Id, _clock.UtcNow, CancellationToken.None);
        resolved.Should().Be(v2.Id);
    }

    [Fact]
    public async Task Resolver_returns_null_when_no_active_plan()
    {
        var resolved = await _resolver.ResolveActiveVersionIdAsync(_location.Id, _clock.UtcNow, CancellationToken.None);
        resolved.Should().BeNull();
    }
}

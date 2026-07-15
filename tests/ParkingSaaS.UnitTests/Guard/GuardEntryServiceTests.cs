using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Contracts.Guard;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Services;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.Infrastructure.Sessions;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Guard;

public sealed class GuardEntryServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly MutableTenantContext _tenant = new();
    private readonly FakeCurrentUser _user;
    private readonly FakeSessionRealtimeNotifier _realtime = new();
    private readonly AppDbContext _db;
    private readonly GuardEntryService _service;
    private ParkingLocation _location = null!;

    public GuardEntryServiceTests()
    {
        _tenant.ScopeTo(_tenantId);
        _user = new FakeCurrentUser
        {
            TenantId = _tenantId,
            UserId = Guid.NewGuid(),
            Roles = new[] { RoleType.Supervisor } // supervisor bypasses per-location assignment
        };
        _db = InMemoryDb.Create(_tenant);

        var tokens = new ParkingTokenService(new EphemeralDataProtectionProvider());
        var urls = Options.Create(new PublicUrlOptions { BaseUrl = "http://test.local" });

        _service = new GuardEntryService(
            _db, _user, new PlateNormalizer(), tokens, new FakeQrCodeGenerator(),
            new FakeActiveRatePlanResolver(), new TestClock(DateTimeOffset.UtcNow), _realtime, urls,
            NullLogger<GuardEntryService>.Instance);

        SeedLocation();
    }

    private void SeedLocation()
    {
        _location = new ParkingLocation(_tenantId, "Lot", "lot", "Asia/Manila", null);
        _db.ParkingLocations.Add(_location);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Records_entry_and_persists_an_active_session()
    {
        var request = new RecordEntryRequest(_location.Id, "ABC 1234", "Car", "Red", null);

        var ticket = await _service.RecordEntryAsync(request, CancellationToken.None);

        ticket.TicketCode.Should().HaveLength(6);
        ticket.PaymentUrl.Should().Contain("/p/");
        ticket.QrCodeDataUri.Should().StartWith("data:image/png");

        var saved = await _db.ParkingSessions.SingleAsync();
        saved.Status.Should().Be(ParkingSessionStatus.ActiveUnpaid);
        saved.PlateNumberNormalized.Should().Be("ABC1234");
        saved.PublicTokenHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Records_entry_broadcasts_a_realtime_event()
    {
        await _service.RecordEntryAsync(new RecordEntryRequest(_location.Id, "ABC 1234", "Car", null, null), CancellationToken.None);

        var saved = await _db.ParkingSessions.SingleAsync();
        _realtime.Calls.Should().ContainSingle();
        _realtime.Last!.TenantId.Should().Be(_tenantId);
        _realtime.Last.ParkingLocationId.Should().Be(_location.Id);
        _realtime.Last.Event.Kind.Should().Be(SessionEventKind.Entered);
        _realtime.Last.Event.SessionId.Should().Be(saved.Id);
    }

    [Fact]
    public async Task Rejects_a_duplicate_active_session_even_with_different_plate_formatting()
    {
        await _service.RecordEntryAsync(new RecordEntryRequest(_location.Id, "ABC 1234", "Car", null, null), CancellationToken.None);

        var act = async () => await _service.RecordEntryAsync(
            new RecordEntryRequest(_location.Id, "abc-1234", "Car", null, null), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Rejects_an_unknown_vehicle_type()
    {
        var act = async () => await _service.RecordEntryAsync(
            new RecordEntryRequest(_location.Id, "ZZZ999", "Spaceship", null, null), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Rejects_entry_at_a_location_outside_the_tenant()
    {
        var act = async () => await _service.RecordEntryAsync(
            new RecordEntryRequest(Guid.NewGuid(), "ABC1234", "Car", null, null), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}

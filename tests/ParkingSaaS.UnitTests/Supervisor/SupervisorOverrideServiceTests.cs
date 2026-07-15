using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ParkingSaaS.Application.Audit;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Supervisor;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Contracts.Supervisor;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Services;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Supervisor;

public sealed class SupervisorOverrideServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly MutableTenantContext _tenant = new();
    private readonly FakeCurrentUser _user;
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 6, 24, 18, 0, 0, TimeSpan.Zero));
    private readonly FakeSessionRealtimeNotifier _realtime = new();
    private readonly AppDbContext _db;
    private readonly SupervisorOverrideService _service;
    private ParkingLocation _location = null!;

    public SupervisorOverrideServiceTests()
    {
        _tenant.ScopeTo(_tenantId);
        _user = new FakeCurrentUser { TenantId = _tenantId, UserId = Guid.NewGuid(), Roles = new[] { RoleType.Supervisor } };
        _db = InMemoryDb.Create(_tenant);
        var audit = new AuditLogger(_db, _user, _clock);
        _service = new SupervisorOverrideService(_db, _user, new PlateNormalizer(), audit, _clock, _realtime, NullLogger<SupervisorOverrideService>.Instance);

        _location = new ParkingLocation(_tenantId, "Lot", "lot", "Asia/Manila", null);
        _db.ParkingLocations.Add(_location);
        _db.SaveChanges();
    }

    private ParkingSession Seed(string plate = "ABC1234")
    {
        var s = ParkingSession.RecordEntry(_tenantId, _location.Id, Guid.NewGuid(), plate, plate, VehicleType.Car, null, _clock.UtcNow.AddHours(-4), null);
        s.AssignTokens("h", "p", "th", "tp");
        _db.ParkingSessions.Add(s);
        _db.SaveChanges();
        return s;
    }

    [Fact]
    public async Task Void_changes_status_and_audits()
    {
        var s = Seed();
        var result = await _service.VoidAsync(new VoidSessionRequest(s.Id, "duplicate entry", "tablet"), "1.2.3.4", CancellationToken.None);

        result.Status.Should().Be(nameof(ParkingSessionStatus.Void));
        var audit = await _db.AuditLogs.SingleAsync();
        audit.Action.Should().Be("SessionVoided");
        audit.Reason.Should().Be("duplicate entry");
        audit.OldValuesJson.Should().Contain("ActiveUnpaid");

        _realtime.Calls.Should().ContainSingle();
        _realtime.Last!.Event.Kind.Should().Be(SessionEventKind.Overridden);
        _realtime.Last.Event.SessionId.Should().Be(s.Id);
    }

    [Fact]
    public async Task Complimentary_sets_fee_override_to_zero()
    {
        var s = Seed();
        var result = await _service.MarkComplimentaryAsync(new ComplimentaryRequest(s.Id, "VIP guest", null), null, CancellationToken.None);

        result.FeeOverride.Should().Be(0m);
        (await _db.ParkingSessions.SingleAsync()).FeeOverride.Should().Be(0m);
    }

    [Fact]
    public async Task Correct_entry_time_updates_and_audits_old_new()
    {
        var s = Seed();
        var newTime = _clock.UtcNow.AddHours(-2);

        await _service.CorrectEntryTimeAsync(new CorrectEntryTimeRequest(s.Id, newTime, "guard logged wrong time", null), null, CancellationToken.None);

        (await _db.ParkingSessions.SingleAsync()).EntryTime.Should().Be(newTime);
        var audit = await _db.AuditLogs.SingleAsync();
        audit.Action.Should().Be("EntryTimeCorrected");
        audit.NewValuesJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Correct_plate_normalizes_and_updates()
    {
        var s = Seed();
        await _service.CorrectPlateAsync(new CorrectPlateRequest(s.Id, "xyz-9876", "misread plate", null), null, CancellationToken.None);

        var saved = await _db.ParkingSessions.SingleAsync();
        saved.PlateNumberNormalized.Should().Be("XYZ9876");
    }

    [Fact]
    public async Task Correct_plate_rejects_active_duplicate()
    {
        var existing = Seed("XYZ9876");
        var target = Seed("ABC1234");

        var act = async () => await _service.CorrectPlateAsync(
            new CorrectPlateRequest(target.Id, "XYZ 9876", "fix", null), null, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Reason_is_required()
    {
        var s = Seed();
        var act = async () => await _service.VoidAsync(new VoidSessionRequest(s.Id, "   ", null), null, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ParkingSaaS.Application.Audit;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Contracts.Guard;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Guard;

public sealed class GuardExitServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly MutableTenantContext _tenant = new();
    private readonly FakeCurrentUser _user;
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 6, 24, 18, 0, 0, TimeSpan.Zero));
    private readonly FakeSessionPricingService _pricing = new();
    private readonly FakeSessionRealtimeNotifier _realtime = new();
    private readonly AppDbContext _db;
    private readonly GuardExitService _service;
    private ParkingLocation _location = null!;

    public GuardExitServiceTests()
    {
        _tenant.ScopeTo(_tenantId);
        _user = new FakeCurrentUser { TenantId = _tenantId, UserId = Guid.NewGuid(), Roles = new[] { RoleType.Supervisor } };
        _db = InMemoryDb.Create(_tenant);
        var audit = new AuditLogger(_db, _user, _clock);
        _service = new GuardExitService(_db, _user, _pricing, audit, _clock, _realtime, NullLogger<GuardExitService>.Instance);

        _location = new ParkingLocation(_tenantId, "Lot", "lot", "Asia/Manila", null);
        _location.UpdateDetails(null, "Asia/Manila", true);
        _db.ParkingLocations.Add(_location);
        _db.SaveChanges();
    }

    private ParkingSession AddSession(decimal totalPaid = 0m, bool exited = false, DateTimeOffset? deadline = null)
    {
        var s = ParkingSession.RecordEntry(_tenantId, _location.Id, Guid.NewGuid(), "ABC1234", "ABC1234", VehicleType.Car, null, _clock.UtcNow.AddHours(-4), null);
        s.AssignTokens("h", "p", "th", "tp");
        if (totalPaid > 0m) s.RegisterPayment(totalPaid, deadline ?? _clock.UtcNow.AddMinutes(15));
        if (exited) s.ApproveExit(Guid.NewGuid(), _clock.UtcNow, 0m, null);
        _db.ParkingSessions.Add(s);
        _db.SaveChanges();
        return s;
    }

    [Fact]
    public async Task Unpaid_session_is_not_paid()
    {
        var s = AddSession();
        _pricing.Result = FeeResults.Of(90m);

        var status = await _service.GetExitStatusAsync(s.Id, CancellationToken.None);

        status.Decision.Should().Be("NotPaid");
        status.Outstanding.Should().Be(90m);
        status.CanApproveExit.Should().BeFalse();
    }

    [Fact]
    public async Task Zero_fee_session_is_free()
    {
        var s = AddSession();
        _pricing.Result = FeeResults.Of(0m);

        var status = await _service.GetExitStatusAsync(s.Id, CancellationToken.None);

        status.Decision.Should().Be("Free");
        status.CanApproveExit.Should().BeTrue();
    }

    [Fact]
    public async Task Fully_paid_session_is_paid()
    {
        var s = AddSession(totalPaid: 90m);
        _pricing.Result = FeeResults.Of(90m);

        var status = await _service.GetExitStatusAsync(s.Id, CancellationToken.None);

        status.Decision.Should().Be("Paid");
        status.Outstanding.Should().Be(0m);
        status.CanApproveExit.Should().BeTrue();
    }

    [Fact]
    public async Task Overstay_requires_additional_payment()
    {
        var s = AddSession(totalPaid: 70m);
        _pricing.Result = FeeResults.Of(90m); // fee grew past what was paid

        var status = await _service.GetExitStatusAsync(s.Id, CancellationToken.None);

        status.Decision.Should().Be("AdditionalPaymentRequired");
        status.Outstanding.Should().Be(20m);
        status.CanApproveExit.Should().BeFalse();
    }

    [Fact]
    public async Task Approve_exit_succeeds_when_paid_and_writes_audit()
    {
        var s = AddSession(totalPaid: 90m);
        _pricing.Result = FeeResults.Of(90m);

        var result = await _service.ApproveExitAsync(new ApproveExitRequest(s.Id, null, "tablet-1", null), "1.2.3.4", CancellationToken.None);

        result.Status.Should().Be(nameof(ParkingSessionStatus.Exited));
        result.FinalFee.Should().Be(90m);

        var saved = await _db.ParkingSessions.SingleAsync(x => x.Id == s.Id);
        saved.Status.Should().Be(ParkingSessionStatus.Exited);

        var audit = await _db.AuditLogs.SingleAsync();
        audit.Action.Should().Be("ExitApproved");
        audit.EntityId.Should().Be(s.Id.ToString());

        _realtime.Calls.Should().ContainSingle();
        _realtime.Last!.ParkingLocationId.Should().Be(_location.Id);
        _realtime.Last.Event.Kind.Should().Be(SessionEventKind.Exited);
        _realtime.Last.Event.SessionId.Should().Be(s.Id);
    }

    [Fact]
    public async Task Approve_exit_is_rejected_when_unpaid()
    {
        var s = AddSession();
        _pricing.Result = FeeResults.Of(90m);

        var act = async () => await _service.ApproveExitAsync(new ApproveExitRequest(s.Id, null, null, null), null, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Supervisor_can_force_exit_with_an_override_reason()
    {
        var s = AddSession();
        _pricing.Result = FeeResults.Of(90m);

        var result = await _service.ApproveExitAsync(
            new ApproveExitRequest(s.Id, null, null, "PayMongo outage — manual release"), "1.2.3.4", CancellationToken.None);

        result.Status.Should().Be(nameof(ParkingSessionStatus.Exited));
        var audit = await _db.AuditLogs.SingleAsync();
        audit.Action.Should().Be("ExitApprovedWithOverride");
        audit.Reason.Should().Contain("outage");
    }

    [Fact]
    public async Task Plain_guard_cannot_force_exit_with_override()
    {
        // A guard assigned to the location, but lacking supervisor rights.
        _user.Roles = new[] { RoleType.Guard };
        _db.UserParkingLocations.Add(new UserParkingLocation(_user.UserId!.Value, _location.Id, _tenantId));
        _db.SaveChanges();

        var s = AddSession();
        _pricing.Result = FeeResults.Of(90m);

        var act = async () => await _service.ApproveExitAsync(
            new ApproveExitRequest(s.Id, null, null, "let me out"), null, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Overdue_session_cannot_be_forced_out_with_supervisor_override()
    {
        var s = AddSession(totalPaid: 70m, deadline: _clock.UtcNow.AddMinutes(-1));
        _pricing.Result = FeeResults.Of(90m);

        var status = await _service.GetExitStatusAsync(s.Id, CancellationToken.None);
        status.Status.Should().Be(nameof(ParkingSessionStatus.OverstayDue));
        status.CanApproveExit.Should().BeFalse();

        var act = async () => await _service.ApproveExitAsync(
            new ApproveExitRequest(s.Id, null, null, "manual release"), null, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("This session is overdue. The outstanding balance must be paid before exit.");
    }

    [Fact]
    public async Task Overdue_session_is_not_paid_even_when_current_fee_equals_amount_paid()
    {
        var s = AddSession(totalPaid: 50m, deadline: _clock.UtcNow.AddMinutes(-1));
        _pricing.Result = FeeResults.Of(50m);

        var status = await _service.GetExitStatusAsync(s.Id, CancellationToken.None);

        status.Status.Should().Be(nameof(ParkingSessionStatus.OverstayDue));
        status.Decision.Should().Be("AdditionalPaymentRequired");
        status.Outstanding.Should().Be(0m);
        status.CanApproveExit.Should().BeFalse();

        var act = async () => await _service.ApproveExitAsync(
            new ApproveExitRequest(s.Id, null, null, null), null, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("This session is overdue. The outstanding balance must be paid before exit.");
    }
}

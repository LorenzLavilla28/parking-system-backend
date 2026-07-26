using FluentAssertions;
using ParkingSaaS.Domain.Common;
using ParkingSaaS.Domain.Sessions;
using Xunit;

namespace ParkingSaaS.UnitTests.Domain;

public sealed class SessionExitTests
{
    private static ParkingSession New()
        => ParkingSession.RecordEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ABC1234", "ABC1234",
            VehicleType.Car, null, DateTimeOffset.UtcNow.AddHours(-4), null);

    [Fact]
    public void Approve_exit_records_final_state()
    {
        var session = New();
        var guardId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        session.ApproveExit(guardId, now, finalFee: 90m, exitPhotoUrl: "s3://exit.jpg");

        session.Status.Should().Be(ParkingSessionStatus.Exited);
        session.ExitedByGuardId.Should().Be(guardId);
        session.ExitTime.Should().Be(now);
        session.FinalFee.Should().Be(90m);
        session.ExitPhotoUrl.Should().Be("s3://exit.jpg");
    }

    [Fact]
    public void Cannot_exit_a_closed_session()
    {
        var session = New();
        session.Void();
        var act = () => session.ApproveExit(Guid.NewGuid(), DateTimeOffset.UtcNow, 0m, null);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("session.closed");
    }

    [Fact]
    public void Overstay_only_transitions_from_paid_window()
    {
        var session = New();
        session.RegisterPayment(70m, DateTimeOffset.UtcNow.AddMinutes(15)); // → PaidExitWindow
        session.MarkOverstay();
        session.Status.Should().Be(ParkingSessionStatus.OverstayDue);
    }

    [Fact]
    public void Effective_status_reports_overstay_before_background_sweep()
    {
        var now = DateTimeOffset.UtcNow;
        var session = New();
        session.RegisterPayment(70m, now.AddMinutes(-1));

        session.EffectiveStatus(now, calculatedFee: 90m).Should().Be(ParkingSessionStatus.OverstayDue);
        session.Status.Should().Be(ParkingSessionStatus.PaidExitWindow);

        session.RefreshTimeBasedStatus(now, calculatedFee: 90m);
        session.Status.Should().Be(ParkingSessionStatus.OverstayDue);
    }

    [Fact]
    public void Expired_exit_window_is_not_overstay_when_paid_amount_covers_the_fee()
    {
        var now = DateTimeOffset.UtcNow;
        var session = New();
        session.RegisterPayment(90m, now.AddMinutes(-1));

        session.EffectiveStatus(now, calculatedFee: 90m).Should().Be(ParkingSessionStatus.PaidExitWindow);

        session.RefreshTimeBasedStatus(now, calculatedFee: 90m);
        session.Status.Should().Be(ParkingSessionStatus.PaidExitWindow);
    }

    [Fact]
    public void Complimentary_zeroes_the_effective_fee()
    {
        var session = New();
        session.MarkComplimentary();
        session.FeeOverride.Should().Be(0m);
        session.Outstanding(calculatedFee: 90m).Should().Be(0m);
        session.EffectiveFee(90m).Should().Be(0m);
    }

    [Fact]
    public void Waive_outstanding_pins_fee_to_amount_paid()
    {
        var session = New();
        session.RegisterPayment(70m, DateTimeOffset.UtcNow.AddMinutes(15));
        session.WaiveOutstanding();
        session.FeeOverride.Should().Be(70m);
        session.Outstanding(calculatedFee: 90m).Should().Be(0m);
    }

    [Fact]
    public void Outstanding_is_never_negative()
    {
        var session = New();
        session.RegisterPayment(100m, DateTimeOffset.UtcNow.AddMinutes(15));
        session.Outstanding(calculatedFee: 90m).Should().Be(0m);
    }

    [Fact]
    public void Correct_entry_time_updates_and_rejects_future()
    {
        var session = New();
        var now = DateTimeOffset.UtcNow;
        var newTime = now.AddHours(-2);
        session.CorrectEntryTime(newTime, now);
        session.EntryTime.Should().Be(newTime);

        var act = () => session.CorrectEntryTime(now.AddHours(1), now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("session.entry_future");
    }

    [Fact]
    public void Correct_plate_updates_raw_and_normalized()
    {
        var session = New();
        session.CorrectPlate("XYZ 9876", "XYZ9876");
        session.PlateNumberRaw.Should().Be("XYZ 9876");
        session.PlateNumberNormalized.Should().Be("XYZ9876");
    }

    [Fact]
    public void Void_rejects_an_exited_session()
    {
        var session = New();
        session.ApproveExit(Guid.NewGuid(), DateTimeOffset.UtcNow, 0m, null);
        var act = () => session.Void();
        act.Should().Throw<DomainException>().Which.Code.Should().Be("session.exited");
    }
}

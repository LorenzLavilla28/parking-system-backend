using FluentAssertions;
using ParkingSaaS.Domain.Common;
using ParkingSaaS.Domain.Sessions;
using Xunit;

namespace ParkingSaaS.UnitTests.Domain;

public sealed class ParkingSessionTests
{
    private static ParkingSession Create()
        => ParkingSession.RecordEntry(
            tenantId: Guid.NewGuid(),
            parkingLocationId: Guid.NewGuid(),
            createdByGuardId: Guid.NewGuid(),
            plateRaw: "ABC 1234",
            plateNormalized: "ABC1234",
            vehicleType: VehicleType.Car,
            notes: " VIP guest ",
            entryTime: DateTimeOffset.UtcNow,
            entryPhotoUrl: null);

    [Fact]
    public void New_session_starts_active_unpaid_with_zero_paid()
    {
        var session = Create();
        session.Status.Should().Be(ParkingSessionStatus.ActiveUnpaid);
        session.IsActive.Should().BeTrue();
        session.TotalPaid.Should().Be(0m);
        session.Notes.Should().Be("VIP guest");
    }

    [Fact]
    public void Entry_without_tenant_is_rejected()
    {
        var act = () => ParkingSession.RecordEntry(
            Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "ABC", "ABC",
            VehicleType.Car, null, DateTimeOffset.UtcNow, null);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("session.tenant_required");
    }

    [Fact]
    public void Entry_without_normalized_plate_is_rejected()
    {
        var act = () => ParkingSession.RecordEntry(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "", "",
            VehicleType.Car, null, DateTimeOffset.UtcNow, null);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("session.plate_required");
    }

    [Fact]
    public void AssignTokens_stores_all_four_values()
    {
        var session = Create();
        session.AssignTokens("ph", "pp", "th", "tp");
        session.PublicTokenHash.Should().Be("ph");
        session.PublicTokenProtected.Should().Be("pp");
        session.TicketCodeHash.Should().Be("th");
        session.TicketCodeProtected.Should().Be("tp");
    }

    [Fact]
    public void RevertToUnpaid_returns_a_payment_pending_session_to_unpaid()
    {
        var session = Create();
        session.MarkPaymentPending();
        session.Status.Should().Be(ParkingSessionStatus.PaymentPending);

        session.RevertToUnpaid();

        session.Status.Should().Be(ParkingSessionStatus.ActiveUnpaid);
    }

    [Fact]
    public void RevertToUnpaid_does_not_disturb_a_paid_session()
    {
        var session = Create();
        session.RegisterPayment(90m, DateTimeOffset.UtcNow.AddMinutes(15));
        session.Status.Should().Be(ParkingSessionStatus.PaidExitWindow);

        session.RevertToUnpaid();

        session.Status.Should().Be(ParkingSessionStatus.PaidExitWindow);
    }

    [Theory]
    [InlineData(ParkingSessionStatus.ActiveUnpaid, true)]
    [InlineData(ParkingSessionStatus.PaymentPending, true)]
    [InlineData(ParkingSessionStatus.PaidExitWindow, true)]
    [InlineData(ParkingSessionStatus.OverstayDue, true)]
    [InlineData(ParkingSessionStatus.Exited, false)]
    [InlineData(ParkingSessionStatus.Void, false)]
    [InlineData(ParkingSessionStatus.Cancelled, false)]
    public void Active_states_are_classified_correctly(ParkingSessionStatus status, bool expected)
        => status.IsActive().Should().Be(expected);
}

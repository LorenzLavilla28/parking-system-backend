using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Contracts.Payments;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Payments;

public sealed class PaymentTrackingServiceTests
{
    [Fact]
    public async Task Search_and_detail_include_payment_and_session_evidence()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new MutableTenantContext();
        tenant.ScopeTo(tenantId);
        var db = InMemoryDb.Create(tenant);
        var location = new ParkingLocation(tenantId, "Lot", "tracking-lot", "Asia/Manila", null);
        db.ParkingLocations.Add(location);
        var session = ParkingSession.RecordEntry(tenantId, location.Id, Guid.NewGuid(), "ABC1234", "ABC1234",
            VehicleType.Car, null, DateTimeOffset.UtcNow.AddHours(-2), null);
        session.AssignTokens("h", "p", "th", "tp");
        db.ParkingSessions.Add(session);
        var payment = Payment.CreateCashPaid(tenantId, session.Id, null, "PHP", 70m,
            "rh", "rp", DateTimeOffset.UtcNow.AddMinutes(-10), "CR-TEST", Guid.NewGuid());
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var service = new PaymentTrackingService(db, new FakeSessionPricingService(), new TestClock(DateTimeOffset.UtcNow));
        var result = await service.SearchAsync(new PaymentQueryRequest { Search = "ABC1234" }, CancellationToken.None);

        result.TotalCount.Should().Be(1);
        result.Items[0].ReceiptNumber.Should().Be("CR-TEST");
        result.Items[0].Provider.Should().Be("Cash");

        var detail = await service.GetAsync(payment.Id, CancellationToken.None);
        detail.Payment.Amount.Should().Be(70m);
        detail.Session.PlateNumberRaw.Should().Be("ABC1234");
        detail.Timeline.Should().Contain(item => item.Type == "paid");
    }
}

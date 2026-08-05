using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Contracts.Payments;
using ParkingSaaS.Domain.Audit;
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

    [Fact]
    public async Task Default_search_hides_cancelled_attempts_but_explicit_filter_preserves_them()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new MutableTenantContext();
        tenant.ScopeTo(tenantId);
        await using var db = InMemoryDb.Create(tenant);
        var location = new ParkingLocation(tenantId, "Lot", "cancelled-lot", "Asia/Manila", null);
        var session = ParkingSession.RecordEntry(tenantId, location.Id, Guid.NewGuid(), "CXL 123", "CXL123",
            VehicleType.Car, null, DateTimeOffset.UtcNow.AddHours(-1), null);
        var cancelled = Payment.CreateOnlinePending(
            tenantId, session.Id, Guid.NewGuid(), "PHP", 100m,
            "cancel-hash", "cancel-protected", "cancel-key");
        cancelled.Cancel();
        db.ParkingLocations.Add(location);
        db.ParkingSessions.Add(session);
        db.Payments.Add(cancelled);
        await db.SaveChangesAsync();
        var service = new PaymentTrackingService(db, new FakeSessionPricingService(), new TestClock(DateTimeOffset.UtcNow));

        var normal = await service.SearchAsync(new PaymentQueryRequest(), CancellationToken.None);
        var auditView = await service.SearchAsync(
            new PaymentQueryRequest { Status = "Cancelled" }, CancellationToken.None);

        normal.TotalCount.Should().Be(0);
        auditView.Items.Should().ContainSingle().Which.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task Override_activity_projects_session_audit_evidence()
    {
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var tenantId = Guid.NewGuid();
        var tenant = new MutableTenantContext();
        tenant.ScopeTo(tenantId);
        await using var db = InMemoryDb.Create(tenant);
        var location = new ParkingLocation(tenantId, "Lot", "override-lot", "Asia/Manila", null);
        var session = ParkingSession.RecordEntry(tenantId, location.Id, Guid.NewGuid(), "OVR 123", "OVR123",
            VehicleType.Car, null, now.AddHours(-2), null);
        db.ParkingLocations.Add(location);
        db.ParkingSessions.Add(session);
        db.AuditLogs.Add(new AuditLog(
            tenantId, null, location.Id, "OutstandingWaived", nameof(ParkingSession), session.Id.ToString(),
            null, null, "Customer service recovery", null, null, now));
        await db.SaveChangesAsync();
        var service = new PaymentTrackingService(db, new FakeSessionPricingService(), new TestClock(now));

        var result = await service.ListOverridesAsync(
            new PaymentOverrideQueryRequest { PageSize = 10 }, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Label.Should().Be("Outstanding balance waived");
        result[0].PlateNumberRaw.Should().Be("OVR 123");
        result[0].Reason.Should().Be("Customer service recovery");
    }
}

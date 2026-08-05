using FluentAssertions;
using ParkingSaaS.Application.Reports;
using ParkingSaaS.Domain.Audit;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.UnitTests.Common;

namespace ParkingSaaS.UnitTests.Reports;

public sealed class DashboardReportServiceTests
{
    [Fact]
    public async Task Custom_range_and_location_filter_drive_period_metrics()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new MutableTenantContext();
        tenant.ScopeTo(tenantId);
        await using var db = InMemoryDb.Create(tenant);
        var clock = new TestClock(new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero));

        var downtown = new ParkingLocation(tenantId, "Downtown", "downtown", "Asia/Manila", null);
        var airport = new ParkingLocation(tenantId, "Airport", "airport", "Asia/Manila", null);
        db.ParkingLocations.AddRange(downtown, airport);
        var downtownSession = ExitedSession(tenantId, downtown.Id, clock.UtcNow.AddDays(-1).AddHours(-3), clock.UtcNow.AddDays(-1).AddHours(-1));
        db.ParkingSessions.Add(downtownSession);
        db.ParkingSessions.Add(ExitedSession(tenantId, airport.Id, clock.UtcNow.AddDays(-1).AddHours(-5), clock.UtcNow.AddDays(-1).AddHours(-2)));
        var cancelled = Payment.CreateOnlinePending(
            tenantId, downtownSession.Id, Guid.NewGuid(), "PHP", 120m,
            "cancelled-report-hash", "cancelled-report-protected", "cancelled-report-key");
        cancelled.CreatedAt = clock.UtcNow.AddDays(-1);
        cancelled.Cancel();
        db.Payments.Add(cancelled);
        db.AuditLogs.Add(new AuditLog(
            tenantId, null, downtown.Id, "ExitApprovedWithOverride", nameof(ParkingSession), downtownSession.Id.ToString(),
            null, null, "Approved by duty supervisor", null, null, clock.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        var service = new DashboardReportService(db, clock, new FakeSessionPricingService());
        var report = await service.GetAsync(
            days: 90,
            parkingLocationId: downtown.Id,
            from: clock.UtcNow.AddDays(-2),
            to: clock.UtcNow,
            CancellationToken.None);

        report.Summary.PeriodEntries.Should().Be(1);
        report.Summary.PeriodExits.Should().Be(1);
        report.Summary.AverageDurationMinutes.Should().Be(120);
        report.Summary.SupervisorOverrides.Should().Be(1);
        report.PaymentMix.Single(item => item.Key == "failed").Count.Should().Be(0);
        report.Revenue.Should().HaveCount(2);
    }

    [Fact]
    public async Task Overstay_balance_is_reported_separately_from_pending_payment_attempts()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new MutableTenantContext();
        tenant.ScopeTo(tenantId);
        await using var db = InMemoryDb.Create(tenant);
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(now);
        var location = new ParkingLocation(tenantId, "Downtown", "downtown", "Asia/Manila", null);
        db.ParkingLocations.Add(location);
        var session = ParkingSession.RecordEntry(
            tenantId, location.Id, Guid.NewGuid(), "ABC 123", "ABC123", VehicleType.Car, null,
            now.AddHours(-5), null);
        session.RegisterPayment(50m, now.AddHours(-2));
        session.MarkOverstay();
        db.ParkingSessions.Add(session);
        await db.SaveChangesAsync();

        var pricing = new FakeSessionPricingService { Result = FeeResults.Of(100m) };
        var service = new DashboardReportService(db, clock, pricing);
        var report = await service.GetAsync(1, null, null, null, CancellationToken.None);

        report.Summary.OverGraceSessions.Should().Be(1);
        report.Summary.OverGraceAmount.Should().Be(50m);
        report.PaymentMix.Single(item => item.Key == "pending").Count.Should().Be(0);
    }

    private static ParkingSession ExitedSession(
        Guid tenantId,
        Guid locationId,
        DateTimeOffset entry,
        DateTimeOffset exit)
    {
        var session = ParkingSession.RecordEntry(
            tenantId,
            locationId,
            Guid.NewGuid(),
            "ABC 123",
            "ABC123",
            VehicleType.Car,
            null,
            entry,
            null);
        session.ApproveExit(Guid.NewGuid(), exit, 0m, null);
        return session;
    }
}

using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ParkingSaaS.Application.Audit;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Contracts.Guard;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.Infrastructure.Sessions;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Guard;

public sealed class GuardCashPaymentServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly MutableTenantContext _tenant = new();
    private readonly FakeCurrentUser _user;
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 6, 24, 18, 0, 0, TimeSpan.Zero));
    private readonly FakeSessionPricingService _pricing = new();
    private readonly ParkingTokenService _tokens = new(new EphemeralDataProtectionProvider());
    private readonly FakeSessionRealtimeNotifier _realtime = new();
    private readonly AppDbContext _db;
    private readonly GuardCashPaymentService _service;

    public GuardCashPaymentServiceTests()
    {
        _tenant.ScopeTo(_tenantId);
        _user = new FakeCurrentUser { TenantId = _tenantId, UserId = Guid.NewGuid(), Roles = new[] { RoleType.Supervisor } };
        _db = InMemoryDb.Create(_tenant);
        var audit = new AuditLogger(_db, _user, _clock);
        _service = new GuardCashPaymentService(_db, _user, _pricing, _tokens, audit, _clock, _realtime, NullLogger<GuardCashPaymentService>.Instance);
    }

    private ParkingSession Seed(bool allowCash = true, decimal totalPaid = 0m)
    {
        var location = new ParkingLocation(_tenantId, "Lot", "lot", "Asia/Manila", null);
        location.UpdateDetails(null, "Asia/Manila", 15, allowCash);
        _db.ParkingLocations.Add(location);
        var s = ParkingSession.RecordEntry(_tenantId, location.Id, Guid.NewGuid(), "ABC1234", "ABC1234", VehicleType.Car, null, _clock.UtcNow.AddHours(-4), null);
        s.AssignTokens("h", "p", "th", "tp");
        if (totalPaid > 0m) s.RegisterPayment(totalPaid, _clock.UtcNow.AddMinutes(15));
        _db.ParkingSessions.Add(s);
        _db.SaveChanges();
        return s;
    }

    [Fact]
    public async Task Records_cash_payment_calculates_change_and_opens_paid_window()
    {
        var s = Seed();
        _pricing.Result = FeeResults.Of(90m);

        var receipt = await _service.RecordCashPaymentAsync(new RecordCashPaymentRequest(s.Id, 100m, "drawer-1"), "1.2.3.4", CancellationToken.None);

        receipt.AmountDue.Should().Be(90m);
        receipt.Change.Should().Be(10m);
        receipt.ReceiptNumber.Should().StartWith("CR-");
        receipt.SessionStatus.Should().Be(nameof(ParkingSessionStatus.PaidExitWindow));

        var payment = await _db.Payments.SingleAsync();
        payment.Provider.Should().Be(PaymentProvider.Cash);
        payment.Amount.Should().Be(90m);

        (await _db.AuditLogs.CountAsync()).Should().Be(1);

        _realtime.Calls.Should().ContainSingle();
        _realtime.Last!.Event.Kind.Should().Be(SessionEventKind.PaymentRecorded);
        _realtime.Last.Event.SessionId.Should().Be(s.Id);
    }

    [Fact]
    public async Task Overstay_cash_charges_only_the_outstanding_balance()
    {
        var s = Seed(totalPaid: 70m);
        _pricing.Result = FeeResults.Of(90m);

        var receipt = await _service.RecordCashPaymentAsync(new RecordCashPaymentRequest(s.Id, 20m, null), null, CancellationToken.None);

        receipt.AmountDue.Should().Be(20m);
        receipt.Change.Should().Be(0m);
        var saved = await _db.ParkingSessions.SingleAsync(x => x.Id == s.Id);
        saved.TotalPaid.Should().Be(90m);
    }

    [Fact]
    public async Task Cash_is_rejected_when_disabled_for_location()
    {
        var s = Seed(allowCash: false);
        _pricing.Result = FeeResults.Of(90m);

        var act = async () => await _service.RecordCashPaymentAsync(new RecordCashPaymentRequest(s.Id, 100m, null), null, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Insufficient_amount_is_rejected()
    {
        var s = Seed();
        _pricing.Result = FeeResults.Of(90m);

        var act = async () => await _service.RecordCashPaymentAsync(new RecordCashPaymentRequest(s.Id, 50m, null), null, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Nothing_due_is_rejected()
    {
        var s = Seed(totalPaid: 90m);
        _pricing.Result = FeeResults.Of(90m);

        var act = async () => await _service.RecordCashPaymentAsync(new RecordCashPaymentRequest(s.Id, 50m, null), null, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }
}

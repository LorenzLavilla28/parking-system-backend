using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Payments;

public sealed class PaymentReconciliationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly MutableTenantContext _tenant = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 6, 24, 18, 0, 0, TimeSpan.Zero));
    private readonly FakePaymentGateway _gateway = new();
    private readonly FakeSessionRealtimeNotifier _realtime = new();
    private readonly FakeSessionPricingService _pricing = new();
    private readonly AppDbContext _db;
    private readonly PaymentReconciliationService _service;

    public PaymentReconciliationServiceTests()
    {
        _tenant.ScopeTo(_tenantId);
        _db = InMemoryDb.Create(_tenant);
        _service = new PaymentReconciliationService(
            _db, _gateway, new PaymentSettler(_db, TestEmail.Queue(_db), _pricing), _pricing, _clock, _realtime,
            Options.Create(new PayMongoOptions { ReconcilePendingOlderThanMinutes = 3 }),
            NullLogger<PaymentReconciliationService>.Instance,
            TestEmail.Queue(_db), new FakeParkingTokenService(), new FakeQrCodeGenerator(),
            Options.Create(new PublicUrlOptions { BaseUrl = "https://parking.test" }));
    }

    private sealed class FakeParkingTokenService : IParkingTokenService
    {
        public string GeneratePublicToken() => "token";
        public string GenerateTicketCode() => "TICKET";
        public string Hash(string value) => value;
        public string Protect(string value) => value;
        public string Unprotect(string protectedValue) => protectedValue;
    }

    private Payment SeedPendingPayment()
    {
        var location = new ParkingLocation(_tenantId, "Lot", "lot", "Asia/Manila", null);
        _db.ParkingLocations.Add(location);
        var session = ParkingSession.RecordEntry(_tenantId, location.Id, Guid.NewGuid(), "ABC1234", "ABC1234", VehicleType.Car, null, _clock.UtcNow.AddHours(-3), null);
        session.AssignTokens("h", "p", "th", "tp");
        session.MarkPaymentPending();
        _db.ParkingSessions.Add(session);

        var quote = new FeeQuote(_tenantId, session.Id, "PHP", 90m, 0m, 90m, _clock.UtcNow, _clock.UtcNow.AddMinutes(10), "[]", null);
        _db.FeeQuotes.Add(quote);

        var payment = Payment.CreateOnlinePending(_tenantId, session.Id, quote.Id, "PHP", 90m, "rh", "rp", "key");
        payment.SetCheckoutSession("cs_test_123", "https://checkout/cs_test_123");
        _db.Payments.Add(payment);
        _db.SaveChanges();
        return payment;
    }

    [Fact]
    public async Task Pending_payment_found_paid_at_provider_is_settled()
    {
        SeedPendingPayment();
        _gateway.StatusResult = new PaymentStatusResult(PaymentStatus.Paid, "pay_recon", 90m, "PHP", "card");

        var summary = await _service.ReconcileAsync(CancellationToken.None);

        summary.PaymentsSettled.Should().Be(1);
        var payment = await _db.Payments.IgnoreQueryFilters().SingleAsync();
        payment.Status.Should().Be(PaymentStatus.Paid);

        var session = await _db.ParkingSessions.IgnoreQueryFilters().SingleAsync();
        session.Status.Should().Be(ParkingSessionStatus.PaidExitWindow);

        _realtime.Calls.Should().ContainSingle();
        _realtime.Last!.Event.Kind.Should().Be(SessionEventKind.PaymentRecorded);
        _realtime.Last.Event.SessionId.Should().Be(session.Id);
    }

    [Fact]
    public async Task Pending_payment_still_pending_at_provider_is_left_alone()
    {
        SeedPendingPayment();
        _gateway.StatusResult = new PaymentStatusResult(PaymentStatus.Pending, null, null, null, null);

        var summary = await _service.ReconcileAsync(CancellationToken.None);

        summary.PaymentsSettled.Should().Be(0);
        var payment = await _db.Payments.IgnoreQueryFilters().SingleAsync();
        payment.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task Expired_checkout_reverts_the_session_to_unpaid_so_it_can_be_paid_again()
    {
        SeedPendingPayment();
        _gateway.StatusResult = new PaymentStatusResult(PaymentStatus.Expired, null, null, null, null);

        var summary = await _service.ReconcileAsync(CancellationToken.None);

        summary.PaymentsFailed.Should().Be(1);
        var payment = await _db.Payments.IgnoreQueryFilters().SingleAsync();
        payment.Status.Should().Be(PaymentStatus.Expired);

        var session = await _db.ParkingSessions.IgnoreQueryFilters().SingleAsync();
        session.Status.Should().Be(ParkingSessionStatus.ActiveUnpaid);

        _realtime.Last!.Event.Kind.Should().Be(SessionEventKind.PaymentAbandoned);
        _realtime.Last.Event.SessionId.Should().Be(session.Id);
    }

    [Fact]
    public async Task Stale_active_quotes_are_expired()
    {
        var location = new ParkingLocation(_tenantId, "Lot", "lot", "Asia/Manila", null);
        _db.ParkingLocations.Add(location);
        var session = ParkingSession.RecordEntry(_tenantId, location.Id, Guid.NewGuid(), "X", "X", VehicleType.Car, null, _clock.UtcNow.AddHours(-3), null);
        session.AssignTokens("h", "p", "th", "tp");
        _db.ParkingSessions.Add(session);
        var stale = new FeeQuote(_tenantId, session.Id, "PHP", 90m, 0m, 90m, _clock.UtcNow.AddMinutes(-20), _clock.UtcNow.AddMinutes(-10), "[]", null);
        _db.FeeQuotes.Add(stale);
        _db.SaveChanges();

        var summary = await _service.ReconcileAsync(CancellationToken.None);

        summary.QuotesExpired.Should().Be(1);
        var quote = await _db.FeeQuotes.IgnoreQueryFilters().SingleAsync();
        quote.Status.Should().Be(FeeQuoteStatus.Expired);
    }

    [Fact]
    public async Task Expired_paid_exit_window_is_marked_overstay_and_broadcast()
    {
        _pricing.Result = FeeResults.Of(100m);
        var location = new ParkingLocation(_tenantId, "Lot", "lot-overstay", "Asia/Manila", null);
        _db.ParkingLocations.Add(location);
        var session = ParkingSession.RecordEntry(_tenantId, location.Id, Guid.NewGuid(), "OVER123", "OVER123",
            VehicleType.Car, null, _clock.UtcNow.AddHours(-3), null);
        session.AssignTokens("h", "p", "th", "tp");
        session.RegisterPayment(90m, _clock.UtcNow.AddMinutes(-1));
        _db.ParkingSessions.Add(session);
        _db.SaveChanges();

        var summary = await _service.ReconcileAsync(CancellationToken.None);

        summary.OverstaysMarked.Should().Be(1);
        session.Status.Should().Be(ParkingSessionStatus.OverstayDue);
        _realtime.Last!.Event.Kind.Should().Be(SessionEventKind.OverstayDue);
    }
}

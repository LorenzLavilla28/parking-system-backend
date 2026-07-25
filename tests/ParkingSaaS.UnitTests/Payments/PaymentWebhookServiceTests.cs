using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.Infrastructure.Sessions;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Payments;

public sealed class PaymentWebhookServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly MutableTenantContext _tenant = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 6, 24, 18, 0, 0, TimeSpan.Zero));
    private readonly ParkingTokenService _tokens = new(new EphemeralDataProtectionProvider());
    private readonly FakePaymentGateway _gateway = new();
    private readonly FakeSessionRealtimeNotifier _realtime = new();
    private readonly AppDbContext _db;
    private readonly PaymentWebhookService _service;

    private ParkingLocation _location = null!;
    private ParkingSession _session = null!;
    private FeeQuote _quote = null!;
    private Payment _payment = null!;

    public PaymentWebhookServiceTests()
    {
        _tenant.ScopeTo(_tenantId);
        _db = InMemoryDb.Create(_tenant);
        _service = new PaymentWebhookService(
            _db, _gateway, new PaymentSettler(_db, TestEmail.Queue(_db), new FakeSessionPricingService()), _tokens, _clock, _realtime, NullLogger<PaymentWebhookService>.Instance);
        Seed();
    }

    private void Seed()
    {
        _location = new ParkingLocation(_tenantId, "Lot", "lot", "Asia/Manila", null);
        _location.UpdateDetails(null, "Asia/Manila", allowCashPayment: true);
        _db.ParkingLocations.Add(_location);

        _session = ParkingSession.RecordEntry(_tenantId, _location.Id, Guid.NewGuid(), "ABC1234", "ABC1234",
            VehicleType.Car, null, _clock.UtcNow.AddHours(-4), null);
        _session.AssignTokens("h", "p", "th", "tp");
        _session.MarkPaymentPending();
        _db.ParkingSessions.Add(_session);

        _quote = new FeeQuote(_tenantId, _session.Id, "PHP", 90m, 0m, 90m, _clock.UtcNow, _clock.UtcNow.AddMinutes(10), "[]", null);
        _db.FeeQuotes.Add(_quote);

        _payment = Payment.CreateOnlinePending(_tenantId, _session.Id, _quote.Id, "PHP", 90m, "rh", "rp", "key");
        _payment.SetCheckoutSession("cs_test_123", "https://checkout/cs_test_123");
        _db.Payments.Add(_payment);

        _db.SaveChanges();
    }

    [Fact]
    public async Task Valid_paid_event_settles_payment_quote_and_session()
    {
        var outcome = await _service.ProcessPayMongoAsync("{}", "sig", CancellationToken.None);

        outcome.Should().Be(WebhookOutcome.Processed);

        var payment = await _db.Payments.IgnoreQueryFilters().SingleAsync();
        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.ProviderPaymentId.Should().Be("pay_1");

        var quote = await _db.FeeQuotes.IgnoreQueryFilters().SingleAsync();
        quote.Status.Should().Be(FeeQuoteStatus.Used);

        var session = await _db.ParkingSessions.IgnoreQueryFilters().SingleAsync();
        session.Status.Should().Be(ParkingSessionStatus.PaidExitWindow);
        session.TotalPaid.Should().Be(90m);
        session.PaidExitDeadline.Should().Be(_clock.UtcNow.AddMinutes(15));

        var evt = await _db.WebhookEvents.SingleAsync();
        evt.ProcessingStatus.Should().Be(WebhookProcessingStatus.Processed);

        // A realtime PaymentRecorded signal fires for the settled session.
        _realtime.Calls.Should().ContainSingle();
        _realtime.Last!.ParkingLocationId.Should().Be(_location.Id);
        _realtime.Last.Event.Kind.Should().Be(SessionEventKind.PaymentRecorded);
        _realtime.Last.Event.SessionId.Should().Be(_session.Id);
    }

    [Fact]
    public async Task Duplicate_event_is_a_no_op()
    {
        await _service.ProcessPayMongoAsync("{}", "sig", CancellationToken.None);
        var second = await _service.ProcessPayMongoAsync("{}", "sig", CancellationToken.None);

        second.Should().Be(WebhookOutcome.Duplicate);

        // Session was not paid twice.
        var session = await _db.ParkingSessions.IgnoreQueryFilters().SingleAsync();
        session.TotalPaid.Should().Be(90m);
        (await _db.WebhookEvents.CountAsync()).Should().Be(1);

        // Only the first delivery broadcasts; the duplicate does not.
        _realtime.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Invalid_signature_is_rejected()
    {
        _gateway.VerificationResult = new WebhookVerificationResult(false, null, null, null, null, null, null, null, null);

        var outcome = await _service.ProcessPayMongoAsync("{}", "bad", CancellationToken.None);

        outcome.Should().Be(WebhookOutcome.InvalidSignature);
        (await _db.WebhookEvents.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Amount_mismatch_fails_the_payment_without_settling()
    {
        _gateway.VerificationResult = _gateway.VerificationResult with { Amount = 50m };

        var outcome = await _service.ProcessPayMongoAsync("{}", "sig", CancellationToken.None);

        outcome.Should().Be(WebhookOutcome.Processed);
        var payment = await _db.Payments.IgnoreQueryFilters().SingleAsync();
        payment.Status.Should().Be(PaymentStatus.Failed);

        var session = await _db.ParkingSessions.IgnoreQueryFilters().SingleAsync();
        session.Status.Should().Be(ParkingSessionStatus.PaymentPending, "the session must not enter the paid window");

        var evt = await _db.WebhookEvents.SingleAsync();
        evt.ProcessingStatus.Should().Be(WebhookProcessingStatus.Failed);
    }

    [Fact]
    public async Task Unknown_checkout_is_ignored()
    {
        _gateway.VerificationResult = _gateway.VerificationResult with { ProviderCheckoutId = "cs_unknown" };

        var outcome = await _service.ProcessPayMongoAsync("{}", "sig", CancellationToken.None);

        outcome.Should().Be(WebhookOutcome.Ignored);
        var evt = await _db.WebhookEvents.SingleAsync();
        evt.ProcessingStatus.Should().Be(WebhookProcessingStatus.Ignored);
    }

    [Fact]
    public async Task Non_paid_event_type_is_ignored()
    {
        _gateway.VerificationResult = _gateway.VerificationResult with { MappedStatus = null, EventType = "checkout_session.active" };

        var outcome = await _service.ProcessPayMongoAsync("{}", "sig", CancellationToken.None);

        outcome.Should().Be(WebhookOutcome.Ignored);
        var payment = await _db.Payments.IgnoreQueryFilters().SingleAsync();
        payment.Status.Should().Be(PaymentStatus.Pending);
    }
}

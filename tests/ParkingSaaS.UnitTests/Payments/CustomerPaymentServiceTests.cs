using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Contracts.Customer;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Emails;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.Infrastructure.Sessions;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Payments;

public sealed class CustomerPaymentServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly MutableTenantContext _tenant = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 6, 24, 18, 0, 0, TimeSpan.Zero));
    private readonly ParkingTokenService _tokens = new(new EphemeralDataProtectionProvider());
    private readonly FakePaymentGateway _gateway = new();
    private readonly FakeSessionRealtimeNotifier _realtime = new();
    private readonly AppDbContext _db;
    private readonly CustomerPaymentService _service;
    private ParkingSession _session = null!;

    public CustomerPaymentServiceTests()
    {
        _tenant.ScopeTo(_tenantId);
        _db = InMemoryDb.Create(_tenant);
        _service = new CustomerPaymentService(
            _db, _gateway, new PaymentSettler(_db, TestEmail.Queue(_db)), _tokens, _clock, _realtime,
            Options.Create(new PublicUrlOptions { BaseUrl = "http://test.local" }),
            NullLogger<CustomerPaymentService>.Instance);
    }

    private FeeQuote SeedQuote(decimal total, DateTimeOffset expires)
    {
        var location = new ParkingLocation(_tenantId, "Lot", "lot", "Asia/Manila", null);
        _db.ParkingLocations.Add(location);
        _session = ParkingSession.RecordEntry(_tenantId, location.Id, Guid.NewGuid(), "ABC1234", "ABC1234", VehicleType.Car, null, _clock.UtcNow.AddHours(-3), null);
        _session.AssignTokens("h", _tokens.Protect("session-token"), "th", "tp");
        _db.ParkingSessions.Add(_session);
        var quote = new FeeQuote(_tenantId, _session.Id, "PHP", total, 0m, total, _clock.UtcNow, expires, "[]", null);
        _db.FeeQuotes.Add(quote);
        _db.SaveChanges();
        return quote;
    }

    [Fact]
    public async Task Create_checkout_persists_pending_payment_and_returns_hosted_url()
    {
        var quote = SeedQuote(90m, _clock.UtcNow.AddMinutes(10));

        var response = await _service.CreateCheckoutAsync(new StartCheckoutRequest(quote.Id, "buyer@example.com"), CancellationToken.None);

        response.CheckoutUrl.Should().Be("https://checkout.paymongo.com/cs_test_123");
        response.Amount.Should().Be(90m);
        _gateway.CreateCalls.Should().Be(1);

        var payment = await _db.Payments.IgnoreQueryFilters().SingleAsync();
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.ProviderCheckoutSessionId.Should().Be("cs_test_123");

        var session = await _db.ParkingSessions.IgnoreQueryFilters().SingleAsync();
        session.Status.Should().Be(ParkingSessionStatus.PaymentPending);

        // Amount sent to the gateway came from the quote, not the client.
        _gateway.LastCreateRequest!.Amount.Should().Be(90m);
    }

    [Fact]
    public async Task Create_checkout_is_idempotent_per_quote()
    {
        var quote = SeedQuote(90m, _clock.UtcNow.AddMinutes(10));

        var first = await _service.CreateCheckoutAsync(new StartCheckoutRequest(quote.Id, null), CancellationToken.None);
        var second = await _service.CreateCheckoutAsync(new StartCheckoutRequest(quote.Id, null), CancellationToken.None);

        _gateway.CreateCalls.Should().Be(1, "the existing open checkout is reused");
        second.PaymentReference.Should().Be(first.PaymentReference);
        (await _db.Payments.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task New_checkout_supersedes_an_unpaid_prior_checkout_for_the_same_session()
    {
        // First attempt, then the customer abandons it and a fresh quote is created.
        var firstQuote = SeedQuote(90m, _clock.UtcNow.AddMinutes(10));
        var first = await _service.CreateCheckoutAsync(new StartCheckoutRequest(firstQuote.Id, null), CancellationToken.None);

        // Provider confirms the prior checkout is still unpaid, so it is safe to release.
        _gateway.StatusResult = new PaymentStatusResult(PaymentStatus.Pending, null, null, null, null);

        var secondQuote = new FeeQuote(_tenantId, _session.Id, "PHP", 90m, 0m, 90m, _clock.UtcNow, _clock.UtcNow.AddMinutes(10), "[]", null);
        _db.FeeQuotes.Add(secondQuote);
        _db.SaveChanges();

        var second = await _service.CreateCheckoutAsync(new StartCheckoutRequest(secondQuote.Id, null), CancellationToken.None);

        // The old checkout was expired at the provider and its payment cancelled, leaving
        // exactly one open payment so a completed old checkout can't double-charge.
        _gateway.ExpireCalls.Should().Be(1);
        second.PaymentReference.Should().NotBe(first.PaymentReference);

        var payments = await _db.Payments.IgnoreQueryFilters().ToListAsync();
        payments.Should().HaveCount(2);
        payments.Count(p => p.Status == PaymentStatus.Cancelled).Should().Be(1);
        payments.Count(p => p.Status == PaymentStatus.Pending).Should().Be(1);
    }

    [Fact]
    public async Task Retrying_never_cancels_a_prior_checkout_that_was_actually_paid()
    {
        var firstQuote = SeedQuote(90m, _clock.UtcNow.AddMinutes(10));
        await _service.CreateCheckoutAsync(new StartCheckoutRequest(firstQuote.Id, null), CancellationToken.None);

        // The customer already paid the first checkout (webhook not caught up yet).
        _gateway.StatusResult = new PaymentStatusResult(PaymentStatus.Paid, "pay_1", 90m, "PHP", "gcash");

        var secondQuote = new FeeQuote(_tenantId, _session.Id, "PHP", 90m, 0m, 90m, _clock.UtcNow, _clock.UtcNow.AddMinutes(10), "[]", null);
        _db.FeeQuotes.Add(secondQuote);
        _db.SaveChanges();

        var act = async () => await _service.CreateCheckoutAsync(new StartCheckoutRequest(secondQuote.Id, null), CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();

        // The paid payment is settled — never cancelled — and no second charge is opened.
        _gateway.CreateCalls.Should().Be(1);
        var payments = await _db.Payments.IgnoreQueryFilters().ToListAsync();
        payments.Should().ContainSingle().Which.Status.Should().Be(PaymentStatus.Paid);
        (await _db.ParkingSessions.IgnoreQueryFilters().SingleAsync()).Status
            .Should().Be(ParkingSessionStatus.PaidExitWindow);
    }

    [Fact]
    public async Task Polling_status_confirms_and_settles_a_payment_paid_at_the_provider()
    {
        var quote = SeedQuote(90m, _clock.UtcNow.AddMinutes(10));
        var checkout = await _service.CreateCheckoutAsync(new StartCheckoutRequest(quote.Id, null), CancellationToken.None);

        // The customer paid on PayMongo; the webhook hasn't arrived, but a status poll now
        // confirms it directly and settles on the spot.
        _gateway.StatusResult = new PaymentStatusResult(PaymentStatus.Paid, "pay_x", 90m, "PHP", "gcash");

        var status = await _service.GetStatusAsync(checkout.PaymentReference, CancellationToken.None);

        status.Status.Should().Be(nameof(PaymentStatus.Paid));
        status.PaidExitDeadline.Should().NotBeNull();
        (await _db.ParkingSessions.IgnoreQueryFilters().SingleAsync()).Status
            .Should().Be(ParkingSessionStatus.PaidExitWindow);
        _realtime.Last!.Event.Kind.Should().Be(SessionEventKind.PaymentRecorded);
    }

    [Fact]
    public async Task Settlement_queues_a_receipt_when_the_customer_left_an_email()
    {
        var quote = SeedQuote(90m, _clock.UtcNow.AddMinutes(10));
        var checkout = await _service.CreateCheckoutAsync(new StartCheckoutRequest(quote.Id, "buyer@example.com"), CancellationToken.None);

        _gateway.StatusResult = new PaymentStatusResult(PaymentStatus.Paid, "pay_x", 90m, "PHP", "gcash");
        await _service.GetStatusAsync(checkout.PaymentReference, CancellationToken.None);

        var email = await _db.Emails.SingleAsync();
        email.Kind.Should().Be(EmailKind.PaymentReceipt);
        email.ToEmail.Should().Be("buyer@example.com");
        email.Status.Should().Be(EmailStatus.Pending);
    }

    [Fact]
    public async Task Settlement_queues_no_receipt_when_no_email_was_given()
    {
        var quote = SeedQuote(90m, _clock.UtcNow.AddMinutes(10));
        var checkout = await _service.CreateCheckoutAsync(new StartCheckoutRequest(quote.Id, null), CancellationToken.None);

        _gateway.StatusResult = new PaymentStatusResult(PaymentStatus.Paid, "pay_x", 90m, "PHP", "gcash");
        await _service.GetStatusAsync(checkout.PaymentReference, CancellationToken.None);

        (await _db.Emails.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Status_can_be_polled_by_reference()
    {
        var quote = SeedQuote(90m, _clock.UtcNow.AddMinutes(10));
        var checkout = await _service.CreateCheckoutAsync(new StartCheckoutRequest(quote.Id, null), CancellationToken.None);

        var status = await _service.GetStatusAsync(checkout.PaymentReference, CancellationToken.None);

        status.Status.Should().Be(nameof(PaymentStatus.Pending));
        status.Amount.Should().Be(90m);
    }

    [Fact]
    public async Task Expired_quote_cannot_be_paid()
    {
        var quote = SeedQuote(90m, _clock.UtcNow.AddMinutes(-1));

        var act = async () => await _service.CreateCheckoutAsync(new StartCheckoutRequest(quote.Id, null), CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Unknown_reference_status_is_not_found()
    {
        var act = async () => await _service.GetStatusAsync("nope", CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }
}

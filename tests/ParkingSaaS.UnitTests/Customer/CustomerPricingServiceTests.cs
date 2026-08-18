using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Customer;
using ParkingSaaS.Application.Pricing;
using ParkingSaaS.Contracts.Customer;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.RatePlans;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.Infrastructure.Sessions;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Customer;

public sealed class CustomerPricingServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly MutableTenantContext _tenant = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 6, 24, 10, 0, 0, TimeSpan.Zero));
    private readonly ParkingTokenService _tokens = new(new EphemeralDataProtectionProvider());
    private readonly AppDbContext _db;
    private readonly CustomerPricingService _service;
    private readonly SessionPricingService _pricing;
    private ParkingLocation _location = null!;
    private RatePlanVersion _version = null!;

    public CustomerPricingServiceTests()
    {
        _tenant.ScopeTo(_tenantId);
        _db = InMemoryDb.Create(_tenant);

        _pricing = new SessionPricingService(_db, new ParkingFeeCalculator());
        _service = new CustomerPricingService(
            _db, _tokens, _pricing, new FakePayMongoCredentialsResolver(), _clock,
            Options.Create(new PricingOptions { FeeQuoteMinutes = 10 }),
            NullLogger<CustomerPricingService>.Instance);

        Seed();
    }

    private void Seed()
    {
        _location = new ParkingLocation(_tenantId, "Lot", "lot", "Asia/Manila", null);
        _db.ParkingLocations.Add(_location);

        var plan = new RatePlan(_tenantId, _location.Id, "Standard");
        plan.Activate();
        var rules = new PricingRules
        {
            Currency = "PHP",
            EntryGraceMinutes = 15,
            Default = new RateBlock { Type = RateType.FirstBlock, FirstHours = 3, FirstAmount = 50m, IncrementAmount = 20m }
        }.Serialize();
        _version = new RatePlanVersion(_tenantId, plan.Id, 1, _clock.UtcNow.AddDays(-1), rules, Guid.NewGuid());

        _db.RatePlans.Add(plan);
        _db.RatePlanVersions.Add(_version);
        _db.SaveChanges();
    }

    private string AddSession(DateTimeOffset entry, Guid? versionId, ParkingSessionStatus status = ParkingSessionStatus.ActiveUnpaid)
    {
        var token = _tokens.GeneratePublicToken();
        var session = ParkingSession.RecordEntry(_tenantId, _location.Id, Guid.NewGuid(), "ABC1234", "ABC1234", VehicleType.Car, null, entry, null);
        if (versionId is { } v) session.SetRatePlanVersion(v);
        session.AssignTokens(_tokens.Hash(token), _tokens.Protect(token), _tokens.Hash("TICKET"), _tokens.Protect("TICKET"));
        // RecordEntry always yields ActiveUnpaid; for tests that need a terminal
        // state we set it via the property's non-public setter (real transitions land in Phase 5).
        if (status != ParkingSessionStatus.ActiveUnpaid)
            typeof(ParkingSession).GetProperty(nameof(ParkingSession.Status))!
                .GetSetMethod(nonPublic: true)!.Invoke(session, new object[] { status });
        _db.ParkingSessions.Add(session);
        _db.SaveChanges();
        return token;
    }

    [Fact]
    public async Task Current_fee_reflects_elapsed_time()
    {
        // Entry 4h1m ago → 50 + 2*20 = 90
        var token = AddSession(_clock.UtcNow.AddMinutes(-241), _version.Id);

        var fee = await _service.GetCurrentFeeAsync(token, CancellationToken.None);

        fee.PricingAvailable.Should().BeTrue();
        fee.TotalAmount.Should().Be(90m);
        fee.Breakdown.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Paid_first_block_remains_covered_during_exit_grace()
    {
        var entry = _clock.UtcNow.AddHours(-3).AddMinutes(-10);
        var token = AddSession(entry, _version.Id);
        var session = await _db.ParkingSessions.IgnoreQueryFilters().SingleAsync();
        session.RegisterPayment(50m, entry.AddHours(3).AddMinutes(15));
        await _db.SaveChangesAsync();

        var fee = await _service.GetCurrentFeeAsync(token, CancellationToken.None);

        fee.TotalAmount.Should().Be(50m);
        fee.Outstanding.Should().Be(0m);
    }

    [Fact]
    public async Task Current_fee_falls_back_to_active_rate_plan_when_no_rate_plan_pinned()
    {
        var token = AddSession(_clock.UtcNow.AddHours(-2), versionId: null);

        var fee = await _service.GetCurrentFeeAsync(token, CancellationToken.None);

        fee.PricingAvailable.Should().BeTrue();
        fee.TotalAmount.Should().Be(50m);
    }

    [Fact]
    public async Task Create_quote_recalculates_and_persists_immutable_quote()
    {
        var token = AddSession(_clock.UtcNow.AddMinutes(-241), _version.Id);

        var quote = await _service.CreateQuoteAsync(new CreateFeeQuoteRequest(token), CancellationToken.None);

        quote.TotalAmount.Should().Be(90m);
        quote.Status.Should().Be(nameof(FeeQuoteStatus.Active));
        quote.ExpiresAt.Should().Be(_clock.UtcNow.AddMinutes(10));

        var stored = await _db.FeeQuotes.IgnoreQueryFilters().SingleAsync();
        stored.TotalAmount.Should().Be(90m);
        stored.ParkingSessionId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Paid_exit_deadline_uses_end_of_first_block_when_paid_inside_it()
    {
        var entry = _clock.UtcNow.AddHours(-1);
        var token = AddSession(entry, _version.Id);
        var session = await _db.ParkingSessions.IgnoreQueryFilters().SingleAsync();

        var deadline = await _pricing.GetPaidExitDeadlineAsync(session, _clock.UtcNow, CancellationToken.None);

        deadline.Should().Be(entry.AddHours(3).AddMinutes(15));
    }

    [Fact]
    public async Task Create_quote_rejects_a_non_active_session()
    {
        var token = AddSession(_clock.UtcNow.AddHours(-2), _version.Id, ParkingSessionStatus.Exited);

        var act = async () => await _service.CreateQuoteAsync(new CreateFeeQuoteRequest(token), CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Create_quote_rejects_unknown_token()
    {
        var act = async () => await _service.CreateQuoteAsync(new CreateFeeQuoteRequest("nope"), CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }
}

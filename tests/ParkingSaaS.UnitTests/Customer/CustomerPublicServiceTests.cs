using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Customer;
using ParkingSaaS.Contracts.Customer;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Services;
using ParkingSaaS.Domain.Tenants;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.Infrastructure.Sessions;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Customer;

public sealed class CustomerPublicServiceTests
{
    private readonly Guid _tenantId;
    private readonly MutableTenantContext _tenant = new();
    private readonly AppDbContext _db;
    private readonly ParkingTokenService _tokens = new(new EphemeralDataProtectionProvider());
    private readonly FakeLookupThrottle _throttle = new();
    private readonly FakeCaptchaVerifier _captcha = new();
    private readonly CustomerPublicService _service;
    private ParkingLocation _location = null!;

    private static readonly PublicClientContext Client = new("slug:1.2.3.4", "1.2.3.4");

    public CustomerPublicServiceTests()
    {
        var tenantRecord = new Tenant("Demo Parking", "demo-parking", SubscriptionPlan.Starter, "PHP", "Asia/Manila");
        _tenantId = tenantRecord.Id;
        _tenant.ScopeTo(_tenantId);
        _db = InMemoryDb.Create(_tenant);
        _service = new CustomerPublicService(
            _db, new PlateNormalizer(), _tokens, _throttle, _captcha,
            new FakeSessionPricingService(), new TestClock(DateTimeOffset.UtcNow),
            NullLogger<CustomerPublicService>.Instance);

        _db.Tenants.Add(tenantRecord);
        _location = new ParkingLocation(_tenantId, "Demo Lot", "demo-lot", "Asia/Manila", "1 Demo St");
        _db.ParkingLocations.Add(_location);
        _db.SaveChanges();
    }

    private string AddActiveSession(string normalizedPlate)
    {
        var token = _tokens.GeneratePublicToken();
        var session = ParkingSession.RecordEntry(
            _tenantId, _location.Id, Guid.NewGuid(), normalizedPlate, normalizedPlate,
            VehicleType.Car, null, DateTimeOffset.UtcNow, null);
        session.AssignTokens(_tokens.Hash(token), _tokens.Protect(token), _tokens.Hash("TICKET"), _tokens.Protect("TICKET"));
        _db.ParkingSessions.Add(session);
        _db.SaveChanges();
        return token;
    }

    [Fact]
    public async Task Unique_match_returns_found_with_the_public_token()
    {
        var token = AddActiveSession("ABC1234");

        var result = await _service.LookupByPlateAsync("demo-lot", new PlateLookupRequest("abc 1234", null), Client, CancellationToken.None);

        result.Outcome.Should().Be("found");
        result.PublicToken.Should().Be(token);
        _throttle.Successes.Should().Be(1);
    }

    [Fact]
    public async Task Multiple_matches_return_ambiguous_without_revealing_tokens()
    {
        AddActiveSession("ABC1234");
        AddActiveSession("ABC1234");

        var result = await _service.LookupByPlateAsync("demo-lot", new PlateLookupRequest("ABC1234", null), Client, CancellationToken.None);

        result.Outcome.Should().Be("multiple");
        result.PublicToken.Should().BeNull();
    }

    [Fact]
    public async Task No_match_returns_generic_not_found_and_records_a_failure()
    {
        var result = await _service.LookupByPlateAsync("demo-lot", new PlateLookupRequest("NOPE000", null), Client, CancellationToken.None);

        result.Outcome.Should().Be("not_found");
        result.PublicToken.Should().BeNull();
        _throttle.Failures.Should().Be(1);
    }

    [Fact]
    public async Task Blocked_client_is_rejected()
    {
        _throttle.Decision = new ThrottleDecision(IsBlocked: true, CaptchaRequired: false, FailedAttempts: 9);

        var act = async () => await _service.LookupByPlateAsync("demo-lot", new PlateLookupRequest("ABC1234", null), Client, CancellationToken.None);

        await act.Should().ThrowAsync<ThrottledException>();
    }

    [Fact]
    public async Task Captcha_required_and_failed_returns_captcha_prompt_without_lookup()
    {
        AddActiveSession("ABC1234");
        _throttle.Decision = new ThrottleDecision(IsBlocked: false, CaptchaRequired: true, FailedAttempts: 3);
        _captcha.Result = false;

        var result = await _service.LookupByPlateAsync("demo-lot", new PlateLookupRequest("ABC1234", null), Client, CancellationToken.None);

        result.Outcome.Should().Be("captcha_required");
        result.CaptchaRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Public_session_view_shows_plate_and_remains_minimal()
    {
        var token = AddActiveSession("ABC1234");

        var view = await _service.GetSessionByTokenAsync(token, CancellationToken.None);

        view.PlateNumber.Should().Be("ABC1234");
        view.VehicleType.Should().Be(nameof(VehicleType.Car));
        view.LocationName.Should().Be("Demo Lot");
        view.PaymentStatus.Should().Be("Unpaid");
    }

    [Fact]
    public async Task Unknown_token_returns_not_found()
    {
        var act = async () => await _service.GetSessionByTokenAsync("nonexistent-token", CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>();
    }
}

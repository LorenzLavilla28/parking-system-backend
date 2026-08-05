using FluentAssertions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Application.Pricing;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Services;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Guard;

public sealed class GuardSessionServiceTests
{
    [Fact]
    public async Task Closed_session_uses_recorded_final_fee_instead_of_live_recalculation()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new MutableTenantContext();
        tenant.ScopeTo(tenantId);
        await using var db = InMemoryDb.Create(tenant);
        var location = new ParkingLocation(tenantId, "Lot", "lot", "Asia/Manila", null);
        db.ParkingLocations.Add(location);
        var session = ParkingSession.RecordEntry(
            tenantId, location.Id, Guid.NewGuid(), "DAH4172", "DAH4172", VehicleType.Car, null,
            new DateTimeOffset(2026, 8, 4, 16, 44, 0, TimeSpan.FromHours(8)), null);
        session.AssignTokens("h", "p", "th", "tp");
        session.ApproveExit(Guid.NewGuid(), new DateTimeOffset(2026, 8, 4, 17, 0, 0, TimeSpan.FromHours(8)), 421m, null);
        db.ParkingSessions.Add(session);
        await db.SaveChangesAsync();

        var user = new FakeCurrentUser { TenantId = tenantId, Roles = new[] { RoleType.TenantAdministrator } };
        var pricing = new FakeSessionPricingService { Result = FeeResults.Of(581m) };
        var service = new GuardSessionService(
            db, user, new PlateNormalizer(), pricing, new TestParkingTokenService(),
            new FakeQrCodeGenerator(), Options.Create(new PublicUrlOptions { BaseUrl = "https://parking.test" }));

        var result = await service.SearchAsync("DAH4172", null, null, false, new(), CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items[0].Status.Should().Be(nameof(ParkingSessionStatus.Exited));
        result.Items[0].CurrentFee.Should().Be(421m);
        result.Items[0].Outstanding.Should().Be(421m);
    }

    private sealed class TestParkingTokenService : IParkingTokenService
    {
        public string GeneratePublicToken() => "token";
        public string GenerateTicketCode() => "ticket";
        public string Hash(string value) => value;
        public string Protect(string value) => value;
        public string Unprotect(string protectedValue) => protectedValue;
    }
}

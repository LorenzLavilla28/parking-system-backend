using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Infrastructure.Identity;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Identity;

public sealed class JwtTokenServiceTests
{
    private static JwtTokenService CreateService()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "ParkingSaaS",
            Audience = "ParkingSaaS",
            SigningKey = "unit-test-signing-key-at-least-32-characters-long!!",
            AccessTokenMinutes = 15
        });
        return new JwtTokenService(options, new TestClock(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Access_token_carries_tenant_role_and_single_location_claims()
    {
        var tenantId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var user = new ApplicationUser(tenantId, "Jane", "Guard", "jane@demo.local", "hash");
        user.AddRole(RoleType.Guard);
        user.AssignLocation(locationId);

        var token = CreateService().CreateAccessToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);

        jwt.Claims.Should().Contain(c => c.Type == AppClaimTypes.TenantId && c.Value == tenantId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == AppClaimTypes.LocationId && c.Value == locationId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == nameof(RoleType.Guard));
    }

    [Fact]
    public void Access_token_omits_location_claim_when_user_has_multiple_locations()
    {
        var user = new ApplicationUser(Guid.NewGuid(), "Sam", "Supervisor", "sam@demo.local", "hash");
        user.AddRole(RoleType.Supervisor);
        user.AssignLocation(Guid.NewGuid());
        user.AssignLocation(Guid.NewGuid());

        var token = CreateService().CreateAccessToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);

        jwt.Claims.Should().NotContain(c => c.Type == AppClaimTypes.LocationId);
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Infrastructure.Identity;

/// <summary>Issues short-lived HMAC-SHA256 signed JWT access tokens.</summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly IDateTime _clock;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IOptions<JwtOptions> options, IDateTime clock)
    {
        _options = options.Value;
        _clock = clock;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public AccessToken CreateAccessToken(ApplicationUser user, Guid tenantId)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            // The token must carry the selected context, not the account's
            // legacy/default tenant. A dual-access account may be switching
            // between a tenant and the platform, so downstream tenancy,
            // auditing, and authorization services must see the active scope.
            new(AppClaimTypes.TenantId, tenantId.ToString()),
            new(AppClaimTypes.PasswordChanged, (!user.MustChangePassword).ToString().ToLowerInvariant()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // A guard/supervisor assigned to exactly one location carries it as a claim
        // so the tenant context can scope to that location without a DB round-trip.
        var assignedLocations = user.LocationAssignments
            .Where(a => a.TenantId == tenantId)
            .Select(a => a.ParkingLocationId)
            .ToArray();
        if (assignedLocations.Length == 1)
            claims.Add(new Claim(AppClaimTypes.LocationId, assignedLocations[0].ToString()));

        foreach (var role in user.Roles.Where(r => r.TenantId == tenantId))
            claims.Add(new Claim(ClaimTypes.Role, RoleNames.ToName(role.Role)));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: _credentials);

        var value = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(value, expires);
    }

    // Compatibility overload for callers that have not yet selected a context.
    public AccessToken CreateAccessToken(ApplicationUser user)
        => CreateAccessToken(user, user.TenantId);
}

using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Application.Abstractions;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>Issues signed JWT access tokens for authenticated staff principals.</summary>
public interface IJwtTokenService
{
    AccessToken CreateAccessToken(ApplicationUser user);
}

namespace ParkingSaaS.Application.Abstractions;

/// <summary>Generates opaque refresh token values and their storage hashes.</summary>
public interface IRefreshTokenService
{
    /// <summary>A new cryptographically-random token value to hand to the client.</summary>
    string GenerateToken();

    /// <summary>Deterministic hash used as the at-rest representation of a token value.</summary>
    string Hash(string token);
}

using System.Security.Cryptography;
using System.Text;
using ParkingSaaS.Application.Abstractions;

namespace ParkingSaaS.Infrastructure.Identity;

/// <summary>
/// Produces high-entropy opaque refresh tokens (256-bit, URL-safe) and the
/// SHA-256 hash stored at rest. Hashing is deterministic so lookups by token
/// value work, while the raw value never touches the database.
/// </summary>
public sealed class RefreshTokenService : IRefreshTokenService
{
    public string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    public string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

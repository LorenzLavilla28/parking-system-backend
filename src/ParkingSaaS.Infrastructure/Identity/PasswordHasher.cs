using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using ParkingSaaS.Application.Abstractions;

namespace ParkingSaaS.Infrastructure.Identity;

/// <summary>
/// PBKDF2 (HMAC-SHA256) password hashing with a per-password random salt.
/// Format: {iterations}.{base64 salt}.{base64 subkey}. Verification is constant
/// time and flags hashes produced with fewer iterations for transparent rehash.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int SubkeySize = 32;
    private const int CurrentIterations = 210_000;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var subkey = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, CurrentIterations, SubkeySize);
        return $"{CurrentIterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(subkey)}";
    }

    public bool Verify(string hash, string password, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password))
            return false;

        var parts = hash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, iterations, expected.Length);
        var matches = CryptographicOperations.FixedTimeEquals(actual, expected);
        if (matches && iterations < CurrentIterations)
            needsRehash = true;
        return matches;
    }
}

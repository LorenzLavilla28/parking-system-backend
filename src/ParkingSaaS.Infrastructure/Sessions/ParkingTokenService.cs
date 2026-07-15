using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using ParkingSaaS.Application.Abstractions;

namespace ParkingSaaS.Infrastructure.Sessions;

/// <summary>
/// Public session token + ticket code generation and securing.
/// - Public token: 256 bits of CSPRNG entropy, base64url.
/// - Ticket code: 6 chars from an unambiguous alphabet (no 0/O/1/I).
/// - Hash: SHA-256 hex for indexed equality lookups.
/// - Protect/Unprotect: ASP.NET Core Data Protection so the original value can
///   be recovered for QR reprinting without storing plaintext.
/// </summary>
public sealed class ParkingTokenService : IParkingTokenService
{
    private const string TicketAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int TicketLength = 6;

    private readonly IDataProtector _protector;

    public ParkingTokenService(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("ParkingSaaS.ParkingTokens.v1");

    public string GeneratePublicToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string GenerateTicketCode()
    {
        var chars = new char[TicketLength];
        for (var i = 0; i < TicketLength; i++)
            chars[i] = TicketAlphabet[RandomNumberGenerator.GetInt32(TicketAlphabet.Length)];
        return new string(chars);
    }

    public string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public string Protect(string value) => _protector.Protect(value);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}

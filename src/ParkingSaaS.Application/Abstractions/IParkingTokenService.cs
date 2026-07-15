namespace ParkingSaaS.Application.Abstractions;

/// <summary>
/// Generates and secures the public session token and human-readable ticket
/// code. Lookups use the deterministic <see cref="Hash"/>; the protected form
/// (<see cref="Protect"/>/<see cref="Unprotect"/>) lets the QR be reprinted
/// without ever storing the raw value in plaintext.
/// </summary>
public interface IParkingTokenService
{
    /// <summary>A cryptographically-random, URL-safe public session token (≥256-bit).</summary>
    string GeneratePublicToken();

    /// <summary>A short, human-readable ticket code from an unambiguous alphabet (no 0/O/1/I).</summary>
    string GenerateTicketCode();

    /// <summary>Deterministic SHA-256 hash used as the indexed lookup key.</summary>
    string Hash(string value);

    /// <summary>Reversibly encrypts a value for at-rest storage (reprint recovery).</summary>
    string Protect(string value);

    /// <summary>Decrypts a value produced by <see cref="Protect"/>.</summary>
    string Unprotect(string protectedValue);
}

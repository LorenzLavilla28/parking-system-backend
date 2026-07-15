namespace ParkingSaaS.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Verifies a password; <paramref name="needsRehash"/> signals an outdated format.</summary>
    bool Verify(string hash, string password, out bool needsRehash);
}

using System.Text;

namespace ParkingSaaS.Domain.Services;

public interface IPlateNormalizer
{
    /// <summary>Produces the canonical form used for storage, indexing, and matching.</summary>
    string Normalize(string rawPlate);
}

/// <summary>
/// Normalizes plate numbers to a canonical comparison form: trim, uppercase,
/// and strip spaces, hyphens, and other formatting so that "ABC 1234",
/// "abc-1234" and "ABC1234" all match. Only A–Z and 0–9 are preserved.
/// The raw plate is always stored alongside the normalized value so a
/// supervisor correction workflow can compare what was actually entered.
/// </summary>
public sealed class PlateNormalizer : IPlateNormalizer
{
    public string Normalize(string rawPlate)
    {
        if (string.IsNullOrWhiteSpace(rawPlate))
            return string.Empty;

        var sb = new StringBuilder(rawPlate.Length);
        foreach (var ch in rawPlate)
        {
            if (char.IsLetterOrDigit(ch) && ch < 128) // ASCII alphanumerics only
                sb.Append(char.ToUpperInvariant(ch));
        }
        return sb.ToString();
    }
}

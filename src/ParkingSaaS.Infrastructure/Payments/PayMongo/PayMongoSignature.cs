using System.Security.Cryptography;
using System.Text;

namespace ParkingSaaS.Infrastructure.Payments.PayMongo;

/// <summary>
/// Verifies the PayMongo webhook signature header of the form
/// <c>t=&lt;timestamp&gt;,te=&lt;test sig&gt;,li=&lt;live sig&gt;</c>. The signed
/// payload is <c>"{timestamp}.{rawBody}"</c>, HMAC-SHA256 with the webhook secret.
/// Comparison is constant time. Isolated here so it can be unit-tested directly.
/// </summary>
public static class PayMongoSignature
{
    public static bool Verify(string rawPayload, string signatureHeader, string webhookSecret, out string? timestamp)
    {
        timestamp = null;
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(webhookSecret))
            return false;

        string? te = null, li = null, t = null;
        foreach (var part in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var key = part[..idx];
            var value = part[(idx + 1)..];
            switch (key)
            {
                case "t": t = value; break;
                case "te": te = value; break;
                case "li": li = value; break;
            }
        }

        if (string.IsNullOrEmpty(t))
            return false;

        timestamp = t;
        var expected = Compute(webhookSecret, $"{t}.{rawPayload}");

        return FixedTimeEquals(expected, te) || FixedTimeEquals(expected, li);
    }

    public static string Compute(string secret, string signedPayload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string expectedHex, string? providedHex)
    {
        if (string.IsNullOrEmpty(providedHex)) return false;
        var a = Encoding.ASCII.GetBytes(expectedHex);
        var b = Encoding.ASCII.GetBytes(providedHex.Trim().ToLowerInvariant());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}

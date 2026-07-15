using FluentAssertions;
using ParkingSaaS.Infrastructure.Payments.PayMongo;
using Xunit;

namespace ParkingSaaS.UnitTests.Payments;

public sealed class PayMongoSignatureTests
{
    private const string Secret = "whsk_test_secret";
    private const string Payload = "{\"data\":{\"id\":\"evt_1\"}}";

    private static string SignedHeader(string timestamp, string payload, string secret)
    {
        var sig = PayMongoSignature.Compute(secret, $"{timestamp}.{payload}");
        return $"t={timestamp},te={sig},li={sig}";
    }

    [Fact]
    public void Valid_signature_passes()
    {
        var header = SignedHeader("1700000000", Payload, Secret);
        PayMongoSignature.Verify(Payload, header, Secret, out var ts).Should().BeTrue();
        ts.Should().Be("1700000000");
    }

    [Fact]
    public void Tampered_payload_fails()
    {
        var header = SignedHeader("1700000000", Payload, Secret);
        PayMongoSignature.Verify(Payload + "tampered", header, Secret, out _).Should().BeFalse();
    }

    [Fact]
    public void Wrong_secret_fails()
    {
        var header = SignedHeader("1700000000", Payload, Secret);
        PayMongoSignature.Verify(Payload, header, "wrong_secret", out _).Should().BeFalse();
    }

    [Fact]
    public void Malformed_header_fails()
        => PayMongoSignature.Verify(Payload, "garbage", Secret, out _).Should().BeFalse();

    [Fact]
    public void Missing_timestamp_fails()
        => PayMongoSignature.Verify(Payload, "te=abc,li=def", Secret, out _).Should().BeFalse();
}

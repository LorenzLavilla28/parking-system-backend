using FluentAssertions;
using ParkingSaaS.Infrastructure.Identity;
using Xunit;

namespace ParkingSaaS.UnitTests.Identity;

public sealed class RefreshTokenServiceTests
{
    private readonly RefreshTokenService _service = new();

    [Fact]
    public void GenerateToken_produces_unique_high_entropy_values()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => _service.GenerateToken()).ToHashSet();
        tokens.Should().HaveCount(1000, "generated tokens must not collide");
        tokens.First().Length.Should().BeGreaterThan(40, "256-bit base64url is ~43 chars");
    }

    [Fact]
    public void Hash_is_deterministic_for_the_same_token()
    {
        var token = _service.GenerateToken();
        _service.Hash(token).Should().Be(_service.Hash(token));
    }

    [Fact]
    public void Hash_differs_for_different_tokens()
    {
        _service.Hash(_service.GenerateToken()).Should().NotBe(_service.Hash(_service.GenerateToken()));
    }

    [Fact]
    public void Hash_does_not_contain_the_raw_token()
    {
        var token = _service.GenerateToken();
        _service.Hash(token).Should().NotContain(token);
    }
}

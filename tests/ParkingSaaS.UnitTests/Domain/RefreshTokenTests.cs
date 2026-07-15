using FluentAssertions;
using ParkingSaaS.Domain.Users;
using Xunit;

namespace ParkingSaaS.UnitTests.Domain;

public sealed class RefreshTokenTests
{
    private static RefreshToken NewToken(DateTimeOffset now)
        => new(Guid.NewGuid(), Guid.NewGuid(), "hash", now, now.AddDays(14), "127.0.0.1");

    [Fact]
    public void Token_is_active_before_expiry_and_when_not_revoked()
    {
        var now = DateTimeOffset.UtcNow;
        NewToken(now).IsActive(now).Should().BeTrue();
    }

    [Fact]
    public void Token_is_inactive_after_expiry()
    {
        var now = DateTimeOffset.UtcNow;
        NewToken(now).IsActive(now.AddDays(15)).Should().BeFalse();
    }

    [Fact]
    public void Revoking_records_replacement_and_deactivates()
    {
        var now = DateTimeOffset.UtcNow;
        var token = NewToken(now);
        token.Revoke(now, "next-hash");

        token.IsActive(now).Should().BeFalse();
        token.ReplacedByTokenHash.Should().Be("next-hash");
        token.RevokedAt.Should().Be(now);
    }
}

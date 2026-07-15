using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using ParkingSaaS.Infrastructure.Sessions;
using Xunit;

namespace ParkingSaaS.UnitTests.Sessions;

public sealed class ParkingTokenServiceTests
{
    private static ParkingTokenService Create()
        => new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Public_tokens_are_unique_and_high_entropy()
    {
        var svc = Create();
        var tokens = Enumerable.Range(0, 1000).Select(_ => svc.GeneratePublicToken()).ToHashSet();
        tokens.Should().HaveCount(1000);
        tokens.First().Length.Should().BeGreaterThan(40);
    }

    [Fact]
    public void Ticket_codes_use_unambiguous_alphabet_and_fixed_length()
    {
        var svc = Create();
        const string allowed = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (var i = 0; i < 200; i++)
        {
            var code = svc.GenerateTicketCode();
            code.Should().HaveLength(6);
            code.All(allowed.Contains).Should().BeTrue();
            code.Should().NotContainAny("0", "O", "1", "I");
        }
    }

    [Fact]
    public void Hash_is_deterministic_and_hides_the_value()
    {
        var svc = Create();
        var token = svc.GeneratePublicToken();
        svc.Hash(token).Should().Be(svc.Hash(token));
        svc.Hash(token).Should().NotContain(token);
    }

    [Fact]
    public void Protect_then_unprotect_round_trips()
    {
        var svc = Create();
        var token = svc.GeneratePublicToken();
        var protectedValue = svc.Protect(token);
        protectedValue.Should().NotBe(token);
        svc.Unprotect(protectedValue).Should().Be(token);
    }
}

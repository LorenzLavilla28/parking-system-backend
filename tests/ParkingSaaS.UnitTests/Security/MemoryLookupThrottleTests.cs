using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Infrastructure.Security;
using Xunit;

namespace ParkingSaaS.UnitTests.Security;

public sealed class MemoryLookupThrottleTests
{
    private static MemoryLookupThrottle Create(out IMemoryCache cache)
    {
        cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new LookupThrottleOptions
        {
            CaptchaAfterFailures = 3,
            BlockAfterFailures = 5,
            BlockMinutes = 15,
            WindowMinutes = 15
        });
        return new MemoryLookupThrottle(cache, options);
    }

    [Fact]
    public void Fresh_client_is_not_blocked_and_needs_no_captcha()
    {
        var throttle = Create(out _);
        var decision = throttle.Evaluate("client-a");
        decision.IsBlocked.Should().BeFalse();
        decision.CaptchaRequired.Should().BeFalse();
    }

    [Fact]
    public void Captcha_is_required_after_threshold_failures()
    {
        var throttle = Create(out _);
        for (var i = 0; i < 3; i++) throttle.RegisterFailure("client-b");

        var decision = throttle.Evaluate("client-b");
        decision.CaptchaRequired.Should().BeTrue();
        decision.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void Client_is_blocked_after_higher_threshold()
    {
        var throttle = Create(out _);
        for (var i = 0; i < 5; i++) throttle.RegisterFailure("client-c");

        throttle.Evaluate("client-c").IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void Success_clears_throttle_state()
    {
        var throttle = Create(out _);
        for (var i = 0; i < 4; i++) throttle.RegisterFailure("client-d");
        throttle.RegisterSuccess("client-d");

        var decision = throttle.Evaluate("client-d");
        decision.CaptchaRequired.Should().BeFalse();
        decision.FailedAttempts.Should().Be(0);
    }
}

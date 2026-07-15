using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;

namespace ParkingSaaS.Infrastructure.Security;

/// <summary>
/// In-memory implementation of the public lookup throttle. Tracks failure counts
/// per client key in a sliding window, requires a CAPTCHA after a threshold, and
/// temporarily blocks after a higher threshold. Suitable for a single instance;
/// a distributed cache (e.g. Redis) would back a multi-node deployment.
/// </summary>
public sealed class MemoryLookupThrottle : ILookupThrottle
{
    private readonly IMemoryCache _cache;
    private readonly LookupThrottleOptions _options;

    public MemoryLookupThrottle(IMemoryCache cache, IOptions<LookupThrottleOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    private sealed class State
    {
        public int Failures;
        public DateTimeOffset? BlockedUntil;
    }

    public ThrottleDecision Evaluate(string clientKey)
    {
        if (!_cache.TryGetValue(Key(clientKey), out State? state) || state is null)
            return new ThrottleDecision(false, false, 0);

        var blocked = state.BlockedUntil.HasValue && state.BlockedUntil.Value > DateTimeOffset.UtcNow;
        var captcha = state.Failures >= _options.CaptchaAfterFailures;
        return new ThrottleDecision(blocked, captcha, state.Failures);
    }

    public void RegisterFailure(string clientKey)
    {
        var state = _cache.GetOrCreate(Key(clientKey), entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromMinutes(_options.WindowMinutes);
            return new State();
        })!;

        state.Failures++;
        if (state.Failures >= _options.BlockAfterFailures)
            state.BlockedUntil = DateTimeOffset.UtcNow.AddMinutes(_options.BlockMinutes);

        _cache.Set(Key(clientKey), state, TimeSpan.FromMinutes(_options.WindowMinutes));
    }

    public void RegisterSuccess(string clientKey) => _cache.Remove(Key(clientKey));

    private static string Key(string clientKey) => $"lookup-throttle:{clientKey}";
}

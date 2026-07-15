namespace ParkingSaaS.Application.Abstractions;

public sealed record ThrottleDecision(bool IsBlocked, bool CaptchaRequired, int FailedAttempts);

/// <summary>
/// Tracks failed public plate-lookup attempts per client and enforces
/// escalating friction: require a CAPTCHA after a few failures, then a
/// temporary block after many. Implementations are keyed by IP and/or
/// location+plate.
/// </summary>
public interface ILookupThrottle
{
    /// <summary>Evaluates the current state for a client before a lookup is attempted.</summary>
    ThrottleDecision Evaluate(string clientKey);

    /// <summary>Records a failed lookup, advancing the client toward CAPTCHA/blocking.</summary>
    void RegisterFailure(string clientKey);

    /// <summary>Clears throttle state after a successful lookup.</summary>
    void RegisterSuccess(string clientKey);
}

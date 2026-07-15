using ParkingSaaS.Application.Abstractions;

namespace ParkingSaaS.UnitTests.Common;

/// <summary>Fixed, advanceable clock for deterministic time-based assertions.</summary>
public sealed class TestClock : IDateTime
{
    public DateTimeOffset UtcNow { get; private set; }

    public TestClock(DateTimeOffset now) => UtcNow = now;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

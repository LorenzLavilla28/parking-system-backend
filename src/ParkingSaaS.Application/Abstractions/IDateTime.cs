namespace ParkingSaaS.Application.Abstractions;

/// <summary>Abstraction over the system clock so time-dependent logic is testable.</summary>
public interface IDateTime
{
    DateTimeOffset UtcNow { get; }
}

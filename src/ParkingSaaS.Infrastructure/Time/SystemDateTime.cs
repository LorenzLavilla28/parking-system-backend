using ParkingSaaS.Application.Abstractions;

namespace ParkingSaaS.Infrastructure.Time;

public sealed class SystemDateTime : IDateTime
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

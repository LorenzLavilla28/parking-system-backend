using System.Text.Json;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Domain.Audit;

namespace ParkingSaaS.Application.Audit;

/// <summary>
/// Default audit logger. Resolves the acting user from the current principal and
/// serializes before/after snapshots to JSON. Adds the entry to the context; the
/// caller's SaveChanges commits it atomically with the audited change.
/// </summary>
public sealed class AuditLogger : IAuditLogger
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTime _clock;

    public AuditLogger(IApplicationDbContext db, ICurrentUser user, IDateTime clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public async Task AddAsync(
        Guid tenantId, Guid? parkingLocationId, string action, string entityType, string entityId,
        object? oldValues, object? newValues, string? reason, AuditContext context, CancellationToken ct)
    {
        var entry = new AuditLog(
            tenantId,
            _user.UserId,
            parkingLocationId,
            action,
            entityType,
            entityId,
            Serialize(oldValues),
            Serialize(newValues),
            reason,
            context.IpAddress,
            context.DeviceInformation,
            _clock.UtcNow);

        await _db.AuditLogs.AddAsync(entry, ct);
    }

    private static string? Serialize(object? value)
        => value is null ? null : JsonSerializer.Serialize(value);
}

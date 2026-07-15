namespace ParkingSaaS.Application.Audit;

/// <summary>Context carried from the request for audit entries.</summary>
public sealed record AuditContext(string? IpAddress, string? DeviceInformation);

/// <summary>
/// Writes audit log entries for sensitive actions. The entry is added to the
/// current unit of work but not saved — the caller commits it in the same
/// transaction as the action it records.
/// </summary>
public interface IAuditLogger
{
    Task AddAsync(
        Guid tenantId,
        Guid? parkingLocationId,
        string action,
        string entityType,
        string entityId,
        object? oldValues,
        object? newValues,
        string? reason,
        AuditContext context,
        CancellationToken ct);
}

using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Audit;

/// <summary>
/// An immutable record of a sensitive action (exit approval, cash payment,
/// override). Captures who did what, the before/after values, the required
/// reason, and request context. Tenant-owned so audit data is isolated.
/// </summary>
public class AuditLog : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? ParkingLocationId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string? OldValuesJson { get; private set; }
    public string? NewValuesJson { get; private set; }
    public string? Reason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? DeviceInformation { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AuditLog() { }

    public AuditLog(
        Guid tenantId, Guid? userId, Guid? parkingLocationId,
        string action, string entityType, string entityId,
        string? oldValuesJson, string? newValuesJson, string? reason,
        string? ipAddress, string? deviceInformation, DateTimeOffset createdAt)
    {
        TenantId = tenantId;
        UserId = userId;
        ParkingLocationId = parkingLocationId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        OldValuesJson = oldValuesJson;
        NewValuesJson = newValuesJson;
        Reason = reason;
        IpAddress = ipAddress;
        DeviceInformation = deviceInformation;
        CreatedAt = createdAt;
    }
}

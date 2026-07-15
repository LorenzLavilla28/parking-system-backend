namespace ParkingSaaS.Domain.Common;

/// <summary>
/// Adds created/updated bookkeeping. Values are stamped by the persistence
/// interceptor, never by callers, so timestamps stay consistent and UTC.
/// </summary>
public abstract class AuditableEntity : AggregateRoot
{
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Implemented by every tenant-owned entity. The persistence layer stamps and
/// filters on <see cref="TenantId"/> so isolation cannot be forgotten in a query.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}

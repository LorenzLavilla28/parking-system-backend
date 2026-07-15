namespace ParkingSaaS.Domain.Common;

/// <summary>
/// Base type for all persisted entities. Uses a GUID surrogate key so that
/// public surfaces never expose sequential identifiers.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public override bool Equals(object? obj)
        => obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>
/// Marker for aggregate roots. Aggregates are the only entities that
/// repositories load and persist directly.
/// </summary>
public abstract class AggregateRoot : Entity
{
}

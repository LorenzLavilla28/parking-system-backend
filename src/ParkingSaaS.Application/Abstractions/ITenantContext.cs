namespace ParkingSaaS.Application.Abstractions;

/// <summary>
/// Ambient tenant/location scope for the current request. Resolved by
/// infrastructure from JWT claims (staff), a location slug, or a public session
/// token (customers). Application and persistence code read this rather than
/// trusting any tenant identifier supplied directly by a client.
/// </summary>
public interface ITenantContext
{
    /// <summary>The resolved tenant. <see cref="Guid.Empty"/> when unresolved or platform-admin.</summary>
    Guid TenantId { get; }

    /// <summary>Set for guards/supervisors scoped to a single location, otherwise null.</summary>
    Guid? ParkingLocationId { get; }

    bool IsPlatformAdministrator { get; }

    /// <summary>True once a tenant has been resolved for the request.</summary>
    bool HasTenant { get; }
}

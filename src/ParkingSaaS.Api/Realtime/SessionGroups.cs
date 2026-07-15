namespace ParkingSaaS.Api.Realtime;

/// <summary>
/// SignalR group-name construction for session broadcasts. Guards subscribe to a
/// location group; supervisors/admins subscribe to the tenant-wide group. Every
/// event is sent to both so a client only needs to join the one that matches its
/// view.
/// </summary>
public static class SessionGroups
{
    public static string Location(Guid parkingLocationId) => $"location:{parkingLocationId}";
    public static string Tenant(Guid tenantId) => $"tenant:{tenantId}";
}

/// <summary>
/// Pure subscription-authorization decisions, split out from <c>SessionsHub</c> so
/// they can be unit-tested without a live connection or database.
/// </summary>
public static class SessionHubAccess
{
    /// <summary>
    /// A caller may watch a location when it belongs to their tenant and they are
    /// either a supervisor/admin (any location in the tenant) or a guard assigned
    /// to that specific location.
    /// </summary>
    public static bool CanJoinLocation(bool isSupervisorOrAdmin, bool locationInTenant, bool isAssignedGuard)
        => locationInTenant && (isSupervisorOrAdmin || isAssignedGuard);

    /// <summary>Only supervisors/admins may watch the whole tenant.</summary>
    public static bool CanJoinTenant(bool isSupervisorOrAdmin) => isSupervisorOrAdmin;
}

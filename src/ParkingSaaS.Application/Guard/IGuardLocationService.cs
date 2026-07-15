using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Application.Guard;

public interface IGuardLocationService
{
    /// <summary>
    /// Locations the current staff member may operate. Guards see only their
    /// assigned locations; supervisors/admins see all active tenant locations.
    /// </summary>
    Task<IReadOnlyList<GuardLocationResponse>> GetMyLocationsAsync(CancellationToken ct);
}

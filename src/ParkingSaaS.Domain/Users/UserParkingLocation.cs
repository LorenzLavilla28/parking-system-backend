using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Users;

/// <summary>
/// Maps a guard or supervisor to a parking location they are allowed to work.
/// Tenant-owned so the assignment itself is isolated and indexable.
/// </summary>
public class UserParkingLocation : Entity, ITenantOwned
{
    public Guid UserId { get; private set; }
    public Guid ParkingLocationId { get; private set; }
    public Guid TenantId { get; private set; }

    private UserParkingLocation() { }

    public UserParkingLocation(Guid userId, Guid parkingLocationId, Guid tenantId)
    {
        UserId = userId;
        ParkingLocationId = parkingLocationId;
        TenantId = tenantId;
    }
}

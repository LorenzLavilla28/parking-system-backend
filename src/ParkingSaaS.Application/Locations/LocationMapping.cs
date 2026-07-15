using ParkingSaaS.Contracts.Locations;
using ParkingSaaS.Domain.Locations;

namespace ParkingSaaS.Application.Locations;

internal static class LocationMapping
{
    public static LocationResponse ToResponse(this ParkingLocation l) => new(
        l.Id,
        l.TenantId,
        l.Name,
        l.Slug,
        l.Address,
        l.Timezone,
        l.Status.ToString(),
        l.ExitGraceMinutes,
        l.AllowCashPayment,
        l.ActiveRatePlanId,
        l.PublicQrCodeUrl,
        l.CreatedAt,
        l.UpdatedAt);
}

using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Application.Pricing;

/// <summary>
/// Computes the current fee for a session using the rate plan version pinned at
/// entry. Returns null when the session has no pinned pricing (no rate plan was
/// configured for the location), so callers can present "pricing unavailable".
/// </summary>
public interface ISessionPricingService
{
    Task<FeeCalculationResult?> CalculateAsync(
        ParkingSession session, DateTimeOffset at, DiscountInput? discount, CancellationToken ct);

    Task<int> GetPaidExitGraceMinutesAsync(ParkingSession session, CancellationToken ct);
}

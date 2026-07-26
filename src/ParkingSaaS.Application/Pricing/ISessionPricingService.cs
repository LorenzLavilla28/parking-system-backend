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

    /// <summary>
    /// Returns the exit deadline for a successful payment. When the payment
    /// covers the first rate block, the deadline is anchored to the end of that
    /// block plus the configured grace period; top-up payments use payment time.
    /// </summary>
    Task<DateTimeOffset> GetPaidExitDeadlineAsync(
        ParkingSession session, DateTimeOffset paidAt, CancellationToken ct);
}

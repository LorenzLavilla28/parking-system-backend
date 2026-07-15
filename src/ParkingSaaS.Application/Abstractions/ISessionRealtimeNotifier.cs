using ParkingSaaS.Contracts.Realtime;

namespace ParkingSaaS.Application.Abstractions;

/// <summary>
/// Broadcasts session-change signals to connected staff. Implemented in the API
/// layer over SignalR; the application layer depends only on this seam. Calls are
/// fire-and-forget — an implementation must never throw into a business
/// transaction, so callers can invoke it after their SaveChanges without a guard.
/// </summary>
public interface ISessionRealtimeNotifier
{
    /// <summary>
    /// Notifies everyone watching the given location or tenant that a session changed.
    /// </summary>
    Task SessionChangedAsync(
        Guid tenantId, Guid parkingLocationId, SessionRealtimeEvent evt, CancellationToken ct = default);
}

using ParkingSaaS.Domain.Payments;

namespace ParkingSaaS.Application.Payments;

/// <summary>
/// Applies a successful payment to its quote and session. Shared by the webhook
/// and reconciliation paths so settlement behaves identically however we learn a
/// payment succeeded. Idempotent: settling an already-paid payment is a no-op.
/// Does not call SaveChanges — the caller owns the transaction boundary.
/// </summary>
public interface IPaymentSettler
{
    /// <summary>
    /// Stages the settlement. Returns the affected session's identity so the caller
    /// can broadcast a realtime signal after it commits, or <c>null</c> when the
    /// payment was already settled (no-op — nothing to broadcast).
    /// </summary>
    Task<SettlementResult?> SettleAsync(
        Payment payment, string providerPaymentId, string? method, DateTimeOffset paidAt, CancellationToken ct);
}

/// <summary>The session affected by a settlement, captured for post-commit broadcasting.</summary>
public sealed record SettlementResult(
    Guid TenantId, Guid ParkingLocationId, Guid SessionId, string PlateNumberRaw, string Status);

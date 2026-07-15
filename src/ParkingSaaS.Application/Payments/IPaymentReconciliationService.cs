namespace ParkingSaaS.Application.Payments;

public sealed record ReconciliationSummary(
    int PaymentsChecked,
    int PaymentsSettled,
    int PaymentsFailed,
    int QuotesExpired,
    int OverstaysMarked);

public interface IPaymentReconciliationService
{
    /// <summary>
    /// Reconciles open payments whose webhook may have been delayed/lost by polling
    /// the provider, and expires fee quotes past their TTL. Safe to run repeatedly.
    /// </summary>
    Task<ReconciliationSummary> ReconcileAsync(CancellationToken ct);
}

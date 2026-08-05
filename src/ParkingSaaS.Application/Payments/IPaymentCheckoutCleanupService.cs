namespace ParkingSaaS.Application.Payments;

/// <summary>
/// Closes any still-open online checkout before a parking session is made
/// unavailable for payment.
/// </summary>
public interface IPaymentCheckoutCleanupService
{
    Task CloseOpenCheckoutsAsync(Guid sessionId, CancellationToken cancellationToken);
}

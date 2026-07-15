namespace ParkingSaaS.Application.Abstractions;

public sealed record EmailDispatchSummary(int Attempted, int Sent, int Failed, int DeadLettered);

/// <summary>
/// Drains due messages from the email outbox, sending each via <see cref="IEmailSender"/>
/// and recording the result. Invoked on a schedule by a background hosted service, and
/// directly by tests. Runs outside any tenant scope.
/// </summary>
public interface IEmailDispatchService
{
    Task<EmailDispatchSummary> DispatchDueAsync(CancellationToken cancellationToken);
}

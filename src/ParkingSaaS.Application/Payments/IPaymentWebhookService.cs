namespace ParkingSaaS.Application.Payments;

public enum WebhookOutcome
{
    Processed = 1,
    Duplicate = 2,
    Ignored = 3,
    InvalidSignature = 4,
    TransientError = 5
}

public interface IPaymentWebhookService
{
    /// <summary>
    /// Verifies, de-duplicates, and processes a raw PayMongo webhook payload.
    /// The outcome tells the controller which HTTP status to return.
    /// </summary>
    Task<WebhookOutcome> ProcessPayMongoAsync(string rawPayload, string signatureHeader, CancellationToken ct);
    Task<WebhookOutcome> ProcessPayMongoAsync(string rawPayload, string signatureHeader, string? webhookToken, CancellationToken ct);
}

namespace ParkingSaaS.Application.Payments;

public sealed record ResolvedPayMongoCredentials(
    string SecretKey,
    string WebhookSecret,
    string? PayMongoAccountId = null);

public interface IPayMongoCredentialsResolver
{
    Task<ResolvedPayMongoCredentials?> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken);
    void Invalidate(Guid tenantId);
}

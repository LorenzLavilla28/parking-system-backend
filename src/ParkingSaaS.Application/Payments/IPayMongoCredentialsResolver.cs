namespace ParkingSaaS.Application.Payments;

public sealed record ResolvedPayMongoCredentials(
    string SecretKey,
    string WebhookSecret,
    bool IsGlobalFallback,
    string? PayMongoAccountId = null);

public interface IPayMongoCredentialsResolver
{
    Task<ResolvedPayMongoCredentials?> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken);
    void Invalidate(Guid tenantId, string environment);
}

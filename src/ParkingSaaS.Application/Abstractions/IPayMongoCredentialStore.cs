namespace ParkingSaaS.Application.Abstractions;

public sealed record PayMongoSecretValues(string SecretKey, string WebhookSecret);

public sealed record PayMongoCredentialValidationResult(
    bool IsValid,
    string? AccountId,
    string? Error);

public interface IPayMongoCredentialStore
{
    Task<string> CreateOrUpdateAsync(
        string secretName,
        PayMongoSecretValues values,
        CancellationToken cancellationToken);

    Task<PayMongoSecretValues?> GetAsync(
        string secretArn,
        CancellationToken cancellationToken);
}

public interface IPayMongoCredentialValidator
{
    Task<PayMongoCredentialValidationResult> ValidateAsync(
        string secretKey,
        CancellationToken cancellationToken);
}

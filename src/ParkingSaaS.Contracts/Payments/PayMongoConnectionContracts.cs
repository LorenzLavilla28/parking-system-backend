namespace ParkingSaaS.Contracts.Payments;

public sealed record ConnectPayMongoRequest(
    string Environment,
    string SecretKey,
    string WebhookSecret,
    string? PayMongoAccountId = null);

public sealed record PayMongoConnectionResponse(
    string Environment,
    string Status,
    string? PayMongoAccountId,
    DateTimeOffset? LastValidatedAt,
    string? LastError,
    string? WebhookUrl);

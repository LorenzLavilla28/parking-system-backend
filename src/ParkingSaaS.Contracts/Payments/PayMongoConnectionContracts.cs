namespace ParkingSaaS.Contracts.Payments;

public sealed record ConnectPayMongoRequest(
    string SecretKey,
    string WebhookSecret,
    string? PayMongoAccountId = null);

public sealed record PayMongoConnectionResponse(
    string Status,
    string? PayMongoAccountId,
    DateTimeOffset? LastValidatedAt,
    string? LastError,
    string? WebhookUrl);

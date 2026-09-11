namespace ParkingSaaS.Contracts.Platform;

public sealed record PlatformAdministratorResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Status,
    bool MustChangePassword,
    DateTimeOffset CreatedAt);

public sealed record InvitePlatformAdministratorRequest(
    string FirstName,
    string LastName,
    string Email);

public sealed record InvitePlatformAdministratorResponse(
    PlatformAdministratorResponse Administrator,
    string? TemporaryPassword,
    bool EmailQueued,
    bool ExistingAccount);

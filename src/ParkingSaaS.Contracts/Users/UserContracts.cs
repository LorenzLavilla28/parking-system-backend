namespace ParkingSaaS.Contracts.Users;

public sealed record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<Guid>? AssignedLocationIds);

public sealed record UpdateUserRequest(
    string FirstName,
    string LastName,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<Guid>? AssignedLocationIds,
    bool IsActive);

public sealed record UserResponse(
    Guid Id,
    Guid TenantId,
    string FirstName,
    string LastName,
    string Email,
    string Status,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<Guid> AssignedLocationIds,
    DateTimeOffset CreatedAt);

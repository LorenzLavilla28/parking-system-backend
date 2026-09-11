namespace ParkingSaaS.Contracts.Auth;

public sealed record LoginRequest(string Email, string Password, Guid? TenantId = null);

public sealed record SwitchContextRequest(Guid TenantId);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record PasswordResetResponse(string Message);

public sealed record AuthUserDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string Email,
    string FullName,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<Guid> AssignedLocationIds,
    bool MustChangePassword,
    string TenantStatus,
    IReadOnlyCollection<AuthContextDto> AvailableContexts);

public sealed record AuthContextDto(
    Guid TenantId,
    string TenantName,
    string TenantStatus,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<Guid> AssignedLocationIds,
    bool IsPlatform);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AuthUserDto User);

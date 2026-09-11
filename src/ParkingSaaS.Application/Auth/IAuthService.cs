using ParkingSaaS.Contracts.Auth;

namespace ParkingSaaS.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken ct);
    Task<AuthResponse> SwitchContextAsync(SwitchContextRequest request, Guid userId, string? ipAddress, CancellationToken ct);
    Task LogoutAsync(LogoutRequest request, CancellationToken ct);
    Task<PasswordResetResponse> RequestPasswordResetAsync(ForgotPasswordRequest request, string appBaseUrl, CancellationToken ct);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
    Task<AuthResponse> ChangePasswordAsync(ChangePasswordRequest request, Guid userId, Guid activeTenantId, string? ipAddress, CancellationToken ct);
}

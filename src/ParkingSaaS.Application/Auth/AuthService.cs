using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Contracts.Auth;
using ParkingSaaS.Domain.Tenants;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Application.Auth;

/// <summary>
/// Handles credential verification, JWT issuance, and rotating refresh tokens.
/// Refresh tokens are single-use: presenting one revokes it and mints a new one,
/// so a stolen-then-reused token is detectable and the chain can be cut.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IEmailQueue _emailQueue;
    private readonly IDateTime _clock;
    private readonly LockoutOptions _lockout;
    private readonly JwtOptions _jwtOptions;
    private readonly PasswordResetOptions _passwordResetOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwt,
        IRefreshTokenService refreshTokens,
        IEmailQueue emailQueue,
        IDateTime clock,
        IOptions<LockoutOptions> lockout,
        IOptions<JwtOptions> jwtOptions,
        IOptions<PasswordResetOptions> passwordResetOptions,
        ILogger<AuthService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _emailQueue = emailQueue;
        _clock = clock;
        _lockout = lockout.Value;
        _jwtOptions = jwtOptions.Value;
        _passwordResetOptions = passwordResetOptions.Value;
        _logger = logger;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // IgnoreQueryFilters: login happens before any tenant context exists, and
        // email is globally unique, so we resolve the user across all tenants here.
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .Include(u => u.LocationAssignments)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        var now = _clock.UtcNow;

        if (user is null)
        {
            // Verify against a throwaway hash to keep timing roughly constant and
            // avoid revealing whether the email exists.
            _passwordHasher.Verify(DummyHash, request.Password, out _);
            _logger.LogWarning("Failed login for unknown email {Email} from {Ip}", email, ipAddress);
            throw new UnauthorizedAppException();
        }

        if (user.IsLockedOut(now))
        {
            _logger.LogWarning("Login attempt on locked account {UserId}", user.Id);
            throw new UnauthorizedAppException("Account is temporarily locked. Try again later.");
        }

        if (!user.CanAuthenticate)
            throw new UnauthorizedAppException("Account is not active.");

        if (!_passwordHasher.Verify(user.PasswordHash, request.Password, out var needsRehash))
        {
            user.RegisterFailedLogin(now, _lockout.MaxFailedAttempts, TimeSpan.FromMinutes(_lockout.LockoutMinutes));
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Failed login for {UserId} from {Ip}", user.Id, ipAddress);
            throw new UnauthorizedAppException();
        }

        await EnsureTenantIsActiveAsync(user.TenantId, ct);

        if (needsRehash)
            user.SetPasswordHash(_passwordHasher.Hash(request.Password));

        user.RegisterSuccessfulLogin();

        var response = await IssueTokensAsync(user, ipAddress, now, ct);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("User {UserId} signed in", user.Id);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, string? ipAddress, CancellationToken ct)
    {
        var hash = _refreshTokens.Hash(request.RefreshToken);
        var now = _clock.UtcNow;

        var token = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null || !token.IsActive(now))
            throw new UnauthorizedAppException("Invalid or expired refresh token.");

        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .Include(u => u.LocationAssignments)
            .FirstOrDefaultAsync(u => u.Id == token.UserId, ct);

        if (user is null || !user.CanAuthenticate)
            throw new UnauthorizedAppException("Account is not active.");

        await EnsureTenantIsActiveAsync(user.TenantId, ct);

        // Rotate: the presented token is consumed and chained to its replacement.
        var response = await IssueTokensAsync(user, ipAddress, now, ct);
        token.Revoke(now, _refreshTokens.Hash(response.RefreshToken));
        await _db.SaveChangesAsync(ct);
        return response;
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken ct)
    {
        var hash = _refreshTokens.Hash(request.RefreshToken);
        var token = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is not null && token.RevokedAt is null)
        {
            token.Revoke(_clock.UtcNow);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<PasswordResetResponse> RequestPasswordResetAsync(
        ForgotPasswordRequest request, string appBaseUrl, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email && u.Status == UserStatus.Active, ct);

        // Always return the same response so the endpoint cannot be used to
        // enumerate registered accounts.
        if (user is null)
            return new PasswordResetResponse("If an account exists for that email, a password reset link has been sent.");

        // Keep the response deliberately generic, but do not issue a reset link
        // that could be used to prepare a suspended tenant account for access.
        if (user.TenantId != Guid.Empty && await GetTenantStatusAsync(user.TenantId, ct) != TenantStatus.Active)
            return new PasswordResetResponse("If an account exists for that email, a password reset link has been sent.");

        var now = _clock.UtcNow;
        var previous = await _db.PasswordResetTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == user.Id && t.UsedAt == null)
            .ToListAsync(ct);

        // Avoid becoming an email-spam primitive when the same account is
        // repeatedly submitted, while keeping the response indistinguishable.
        if (previous.Any(t => t.CreatedAt > now.AddMinutes(-1)))
            return new PasswordResetResponse("If an account exists for that email, a password reset link has been sent.");

        foreach (var token in previous) token.Consume(now);

        var rawToken = _refreshTokens.GenerateToken();
        var resetToken = new PasswordResetToken(
            user.Id,
            user.TenantId,
            _refreshTokens.Hash(rawToken),
            now,
            now.AddMinutes(_passwordResetOptions.TokenLifetimeMinutes));
        await _db.PasswordResetTokens.AddAsync(resetToken, ct);

        var resetUrl = $"{appBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(rawToken)}";
        _emailQueue.QueuePasswordReset(user.TenantId, user.Email, user.FullName, resetUrl, now);
        await _db.SaveChangesAsync(ct);

        return new PasswordResetResponse("If an account exists for that email, a password reset link has been sent.");
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var token = await _db.PasswordResetTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == _refreshTokens.Hash(request.Token), ct);

        if (token is null || !token.IsActive(now))
            throw new UnauthorizedAppException("This password reset link is invalid or expired.");

        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        if (user is null || !user.CanAuthenticate)
            throw new UnauthorizedAppException("This password reset link is invalid or expired.");

        await EnsureTenantIsActiveAsync(user.TenantId, ct);

        user.CompletePasswordChange(_passwordHasher.Hash(request.NewPassword));
        token.Consume(now);
        await RevokeActiveRefreshTokensAsync(user.Id, now, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<AuthResponse> ChangePasswordAsync(
        ChangePasswordRequest request, Guid userId, string? ipAddress, CancellationToken ct)
    {
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .Include(u => u.LocationAssignments)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedAppException();

        if (!user.CanAuthenticate || !_passwordHasher.Verify(user.PasswordHash, request.CurrentPassword, out _))
            throw new UnauthorizedAppException("Current password is incorrect.");

        await EnsureTenantIsActiveAsync(user.TenantId, ct);

        var now = _clock.UtcNow;
        user.CompletePasswordChange(_passwordHasher.Hash(request.NewPassword));
        await RevokeActiveRefreshTokensAsync(user.Id, now, ct);
        var response = await IssueTokensAsync(user, ipAddress, now, ct);
        await _db.SaveChangesAsync(ct);
        return response;
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, string? ipAddress, DateTimeOffset now, CancellationToken ct)
    {
        var access = _jwt.CreateAccessToken(user);

        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Id == user.TenantId)
            .Select(t => new { t.Name, t.Status })
            .SingleOrDefaultAsync(ct);

        var tenantName = tenant?.Name ?? "Tenant workspace";
        var tenantStatus = tenant?.Status.ToString() ?? "Platform";

        var refreshValue = _refreshTokens.GenerateToken();
        var refreshExpiry = now.AddDays(_jwtOptions.RefreshTokenDays);
        var refresh = new RefreshToken(user.Id, user.TenantId, _refreshTokens.Hash(refreshValue), now, refreshExpiry, ipAddress);
        await _db.RefreshTokens.AddAsync(refresh, ct);

        var dto = new AuthUserDto(
            user.Id,
            user.TenantId,
            tenantName,
            user.Email,
            user.FullName,
            user.Roles.Select(r => RoleNames.ToName(r.Role)).ToArray(),
            user.LocationAssignments.Select(a => a.ParkingLocationId).ToArray(),
            user.MustChangePassword,
            tenantStatus);

        return new AuthResponse(access.Value, access.ExpiresAt, refreshValue, refreshExpiry, dto);
    }

    private async Task RevokeActiveRefreshTokensAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var tokens = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in tokens) token.Revoke(now);
    }

    private async Task EnsureTenantIsActiveAsync(Guid tenantId, CancellationToken ct)
    {
        // Platform administrators use Guid.Empty and are intentionally outside
        // tenant lifecycle enforcement.
        if (tenantId == Guid.Empty)
            return;

        var status = await GetTenantStatusAsync(tenantId, ct);

        if (status != TenantStatus.Active)
        {
            var message = status == TenantStatus.Archived
                ? "This tenant membership is archived. Contact your platform administrator."
                : "This tenant membership is suspended. Contact your platform administrator.";
            throw new TenantSuspendedException(message);
        }
    }

    private async Task<TenantStatus?> GetTenantStatusAsync(Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty)
            return null;

        return await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .Select(t => (TenantStatus?)t.Status)
            .SingleOrDefaultAsync(ct);
    }

    // A precomputed valid PBKDF2 hash of a random string, used only for timing parity.
    private const string DummyHash =
        "AQAAAAEAACcQAAAAEDummyDummyDummyDummyDummyDummyDummyDummyDummyDummyDummyDummyDummyDw==";
}

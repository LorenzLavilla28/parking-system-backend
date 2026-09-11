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
/// Handles credential verification, scoped membership selection, JWT issuance,
/// and rotating refresh tokens. An account has one credential set, while each
/// token is bound to exactly one active tenant or platform membership.
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
        var user = await LoadUserAsync(email, ct);
        var now = _clock.UtcNow;

        if (user is null)
        {
            _passwordHasher.Verify(DummyHash, request.Password, out _);
            _logger.LogWarning("Failed login for unknown email {Email} from {Ip}", email, ipAddress);
            throw new UnauthorizedAppException();
        }

        if (user.IsLockedOut(now))
            throw new UnauthorizedAppException("Account is temporarily locked. Try again later.");
        if (!user.CanAuthenticate)
            throw new UnauthorizedAppException("Account is not active.");

        if (!_passwordHasher.Verify(user.PasswordHash, request.Password, out var needsRehash))
        {
            user.RegisterFailedLogin(now, _lockout.MaxFailedAttempts, TimeSpan.FromMinutes(_lockout.LockoutMinutes));
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Failed login for {UserId} from {Ip}", user.Id, ipAddress);
            throw new UnauthorizedAppException();
        }

        var membership = await ResolveMembershipAsync(user, request.TenantId, ct);
        if (needsRehash)
            user.SetPasswordHash(_passwordHasher.Hash(request.Password));
        user.RegisterSuccessfulLogin();

        var response = await IssueTokensAsync(user, membership.TenantId, ipAddress, now, ct);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("User {UserId} signed in using tenant context {TenantId}", user.Id, membership.TenantId);
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

        var user = await LoadUserAsync(token.UserId, ct);
        if (user is null || !user.CanAuthenticate)
            throw new UnauthorizedAppException("Account is not active.");

        var membership = await ResolveMembershipAsync(user, token.TenantId, ct);
        var response = await IssueTokensAsync(user, membership.TenantId, ipAddress, now, ct);
        token.Revoke(now, _refreshTokens.Hash(response.RefreshToken));
        await _db.SaveChangesAsync(ct);
        return response;
    }

    public async Task<AuthResponse> SwitchContextAsync(
        SwitchContextRequest request, Guid userId, string? ipAddress, CancellationToken ct)
    {
        var user = await LoadUserAsync(userId, ct)
            ?? throw new UnauthorizedAppException();
        if (!user.CanAuthenticate)
            throw new UnauthorizedAppException("Account is not active.");

        var membership = await ResolveMembershipAsync(user, request.TenantId, ct);
        var response = await IssueTokensAsync(user, membership.TenantId, ipAddress, _clock.UtcNow, ct);
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
            .Include(u => u.Roles)
            .Include(u => u.Memberships)
            .FirstOrDefaultAsync(u => u.Email == email && u.Status == UserStatus.Active, ct);

        if (user is null)
            return GenericPasswordResetResponse();

        UserMembership membership;
        try
        {
            membership = await ResolveMembershipAsync(user, null, ct);
        }
        catch (Exception ex) when (ex is UnauthorizedAppException or TenantSuspendedException)
        {
            return GenericPasswordResetResponse();
        }

        var now = _clock.UtcNow;
        var previous = await _db.PasswordResetTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == user.Id && t.UsedAt == null)
            .ToListAsync(ct);

        if (previous.Any(t => t.CreatedAt > now.AddMinutes(-1)))
            return GenericPasswordResetResponse();

        foreach (var token in previous) token.Consume(now);

        var rawToken = _refreshTokens.GenerateToken();
        var resetToken = new PasswordResetToken(
            user.Id,
            membership.TenantId,
            _refreshTokens.Hash(rawToken),
            now,
            now.AddMinutes(_passwordResetOptions.TokenLifetimeMinutes));
        await _db.PasswordResetTokens.AddAsync(resetToken, ct);

        var resetUrl = $"{appBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(rawToken)}";
        _emailQueue.QueuePasswordReset(membership.TenantId, user.Email, user.FullName, resetUrl, now);
        await _db.SaveChangesAsync(ct);

        return GenericPasswordResetResponse();
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var token = await _db.PasswordResetTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == _refreshTokens.Hash(request.Token), ct);

        if (token is null || !token.IsActive(now))
            throw new UnauthorizedAppException("This password reset link is invalid or expired.");

        var user = await LoadUserAsync(token.UserId, ct);
        if (user is null || !user.CanAuthenticate)
            throw new UnauthorizedAppException("This password reset link is invalid or expired.");

        _ = await ResolveMembershipAsync(user, null, ct);
        user.CompletePasswordChange(_passwordHasher.Hash(request.NewPassword));
        token.Consume(now);
        await RevokeActiveRefreshTokensAsync(user.Id, now, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<AuthResponse> ChangePasswordAsync(
        ChangePasswordRequest request, Guid userId, Guid activeTenantId, string? ipAddress, CancellationToken ct)
    {
        var user = await LoadUserAsync(userId, ct)
            ?? throw new UnauthorizedAppException();

        if (!user.CanAuthenticate || !_passwordHasher.Verify(user.PasswordHash, request.CurrentPassword, out _))
            throw new UnauthorizedAppException("Current password is incorrect.");

        var membership = await ResolveMembershipAsync(user, activeTenantId, ct);
        var now = _clock.UtcNow;
        user.CompletePasswordChange(_passwordHasher.Hash(request.NewPassword));
        await RevokeActiveRefreshTokensAsync(user.Id, now, ct);
        var response = await IssueTokensAsync(user, membership.TenantId, ipAddress, now, ct);
        await _db.SaveChangesAsync(ct);
        return response;
    }

    private async Task<ApplicationUser?> LoadUserAsync(string email, CancellationToken ct)
        => await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .Include(u => u.Memberships)
            .Include(u => u.LocationAssignments)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

    private async Task<ApplicationUser?> LoadUserAsync(Guid userId, CancellationToken ct)
        => await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .Include(u => u.Memberships)
            .Include(u => u.LocationAssignments)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

    private async Task<UserMembership> ResolveMembershipAsync(
        ApplicationUser user, Guid? requestedTenantId, CancellationToken ct)
    {
        var candidates = user.Memberships
            .Where(m => m.IsActive && user.Roles.Any(r => r.TenantId == m.TenantId))
            .ToArray();

        UserMembership? membership;
        if (requestedTenantId.HasValue)
        {
            membership = candidates.FirstOrDefault(m => m.TenantId == requestedTenantId.Value);
        }
        else
        {
            // Do not let a suspended legacy/default tenant hide another active
            // membership. Platform remains the preferred context, followed by
            // the legacy tenant only when it is active, then any other active
            // tenant membership.
            var activeTenantIds = await _db.Tenants
                .IgnoreQueryFilters()
                .Where(t => t.Status == TenantStatus.Active)
                .Select(t => t.Id)
                .ToArrayAsync(ct);
            var activeCandidates = candidates
                .Where(m => m.TenantId == Guid.Empty || activeTenantIds.Contains(m.TenantId))
                .ToArray();

            membership = activeCandidates.FirstOrDefault(m => m.TenantId == Guid.Empty)
                ?? activeCandidates.FirstOrDefault(m => m.TenantId == user.TenantId)
                ?? activeCandidates.FirstOrDefault()
                // Preserve the useful suspended/archived error when no active
                // context remains, rather than returning a generic 401.
                ?? candidates.FirstOrDefault();
        }

        if (membership is null)
            throw new UnauthorizedAppException("No active workspace is available for this account.");

        await EnsureTenantIsActiveAsync(membership.TenantId, ct);
        return membership;
    }

    private async Task<AuthResponse> IssueTokensAsync(
        ApplicationUser user, Guid activeTenantId, string? ipAddress, DateTimeOffset now, CancellationToken ct)
    {
        var membership = await ResolveMembershipAsync(user, activeTenantId, ct);
        var access = _jwt.CreateAccessToken(user, membership.TenantId);

        var tenant = membership.TenantId == Guid.Empty
            ? null
            : await _db.Tenants
                .IgnoreQueryFilters()
                .Where(t => t.Id == membership.TenantId)
                .Select(t => new { t.Name, t.Status })
                .SingleOrDefaultAsync(ct);

        var tenantName = tenant?.Name ?? "Platform Console";
        var tenantStatus = tenant?.Status.ToString() ?? "Platform";
        var refreshValue = _refreshTokens.GenerateToken();
        var refreshExpiry = now.AddDays(_jwtOptions.RefreshTokenDays);
        await _db.RefreshTokens.AddAsync(
            new RefreshToken(user.Id, membership.TenantId, _refreshTokens.Hash(refreshValue), now, refreshExpiry, ipAddress), ct);

        var contexts = await BuildContextsAsync(user, ct);
        var roles = user.Roles
            .Where(r => r.TenantId == membership.TenantId)
            .Select(r => RoleNames.ToName(r.Role))
            .Distinct()
            .ToArray();
        var locations = user.LocationAssignments
            .Where(a => a.TenantId == membership.TenantId)
            .Select(a => a.ParkingLocationId)
            .Distinct()
            .ToArray();

        var dto = new AuthUserDto(
            user.Id,
            membership.TenantId,
            tenantName,
            user.Email,
            user.FullName,
            roles,
            locations,
            user.MustChangePassword,
            tenantStatus,
            contexts);

        return new AuthResponse(access.Value, access.ExpiresAt, refreshValue, refreshExpiry, dto);
    }

    private async Task<IReadOnlyCollection<AuthContextDto>> BuildContextsAsync(
        ApplicationUser user, CancellationToken ct)
    {
        var tenantIds = user.Memberships
            .Where(m => m.TenantId != Guid.Empty)
            .Select(m => m.TenantId)
            .Distinct()
            .ToArray();
        var tenants = await _db.Tenants
            .IgnoreQueryFilters()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.Status })
            .ToDictionaryAsync(t => t.Id, ct);

        return user.Memberships
            .Where(m => m.IsActive
                && (m.TenantId == Guid.Empty
                    || (tenants.TryGetValue(m.TenantId, out var tenant) && tenant.Status == TenantStatus.Active))
                && user.Roles.Any(r => r.TenantId == m.TenantId))
            .OrderByDescending(m => m.TenantId == Guid.Empty)
            .ThenBy(m => m.TenantId)
            .Select(m =>
            {
                var tenant = m.TenantId == Guid.Empty || !tenants.TryGetValue(m.TenantId, out var found)
                    ? null
                    : found;
                return new AuthContextDto(
                    m.TenantId,
                    tenant?.Name ?? "Platform Console",
                    tenant?.Status.ToString() ?? "Platform",
                    user.Roles.Where(r => r.TenantId == m.TenantId).Select(r => RoleNames.ToName(r.Role)).Distinct().ToArray(),
                    user.LocationAssignments.Where(a => a.TenantId == m.TenantId).Select(a => a.ParkingLocationId).Distinct().ToArray(),
                    m.TenantId == Guid.Empty);
            })
            .ToArray();
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
        if (tenantId == Guid.Empty) return;

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
        => tenantId == Guid.Empty
            ? null
            : await _db.Tenants
                .IgnoreQueryFilters()
                .Where(t => t.Id == tenantId)
                .Select(t => (TenantStatus?)t.Status)
                .SingleOrDefaultAsync(ct);

    private static PasswordResetResponse GenericPasswordResetResponse()
        => new("If an account exists for that email, a password reset link has been sent.");

    private const string DummyHash =
        "AQAAAAEAACcQAAAAEDummyDummyDummyDummyDummyDummyDummyDummyDummyDummyDummyDummyDummyDw==";
}

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Contracts.Platform;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Application.Platform;

/// <summary>
/// Platform-level management of the platform-scoped memberships that grant
/// access to the Platform Console. An existing global account is reused rather
/// than duplicated, so one email can safely hold tenant and platform access.
/// </summary>
public sealed class PlatformAdministratorService : IPlatformAdministratorService
{
    private const int TemporaryPasswordLength = 16;
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%";
    private static readonly string PasswordAlphabet = Upper + Lower + Digits + Symbols;

    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailQueue _emailQueue;
    private readonly IDateTime _clock;

    public PlatformAdministratorService(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        IEmailQueue emailQueue,
        IDateTime clock)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _emailQueue = emailQueue;
        _clock = clock;
    }

    public async Task<IReadOnlyList<PlatformAdministratorResponse>> ListAsync(CancellationToken ct)
    {
        var users = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .Include(u => u.Memberships)
            .AsNoTracking()
            .Where(u => u.Roles.Any(r => r.TenantId == Guid.Empty && r.Role == RoleType.PlatformAdministrator))
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync(ct);

        return users.Select(ToResponse).ToArray();
    }

    public async Task<InvitePlatformAdministratorResponse> InviteAsync(
        InvitePlatformAdministratorRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var existing = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .Include(u => u.Memberships)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (existing is not null)
        {
            if (!existing.CanAuthenticate)
                throw new ConflictException("This account is disabled and cannot receive platform access.");

            var platformMembership = existing.Memberships.FirstOrDefault(m => m.TenantId == Guid.Empty);
            var newPlatformMembership = platformMembership is null;
            var alreadyAdministrator = existing.Roles.Any(r =>
                r.TenantId == Guid.Empty && r.Role == RoleType.PlatformAdministrator);
            if (alreadyAdministrator && platformMembership?.IsActive == true)
                throw new ConflictException("This account is already a platform administrator.");

            platformMembership ??= new UserMembership(existing.Id, Guid.Empty);
            platformMembership.Enable();

            if (newPlatformMembership)
                _db.UserMemberships.Add(platformMembership);

            if (!alreadyAdministrator)
                _db.UserRoles.Add(new UserRole(existing.Id, RoleType.PlatformAdministrator, Guid.Empty));

            _emailQueue.QueuePlatformAdministratorAccessGranted(existing.Email, existing.FullName, _clock.UtcNow);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                throw new ConflictException("This account already has platform access.");
            }

            return new InvitePlatformAdministratorResponse(
                ToResponse(existing), null, EmailQueued: true, ExistingAccount: true);
        }

        var temporaryPassword = GenerateTemporaryPassword();
        var administrator = new ApplicationUser(
            Guid.Empty,
            request.FirstName,
            request.LastName,
            email,
            _passwordHasher.Hash(temporaryPassword),
            mustChangePassword: true);
        administrator.AddRole(RoleType.PlatformAdministrator);

        await _db.Users.AddAsync(administrator, ct);
        _emailQueue.QueuePlatformAdministratorInvitation(
            administrator.Email, administrator.FullName, temporaryPassword, _clock.UtcNow);
        try
        {
            // The pre-check gives a fast, friendly response for the common case;
            // the database unique index is still authoritative when two invites
            // for the same address arrive concurrently.
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ConflictException("A user with this email already exists.");
        }

        return new InvitePlatformAdministratorResponse(ToResponse(administrator), temporaryPassword, EmailQueued: true, ExistingAccount: false);
    }

    private static PlatformAdministratorResponse ToResponse(ApplicationUser user)
        => new(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.Memberships.FirstOrDefault(m => m.TenantId == Guid.Empty)?.Status.ToString()
                ?? user.Status.ToString(),
            user.MustChangePassword,
            user.CreatedAt);

    private static string GenerateTemporaryPassword()
    {
        var password = new char[TemporaryPasswordLength];
        password[0] = Pick(Upper);
        password[1] = Pick(Lower);
        password[2] = Pick(Digits);
        password[3] = Pick(Symbols);

        for (var i = 4; i < password.Length; i++)
            password[i] = Pick(PasswordAlphabet);

        for (var i = password.Length - 1; i > 0; i--)
        {
            var swapIndex = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[swapIndex]) = (password[swapIndex], password[i]);
        }

        return new string(password);
    }

    private static char Pick(string alphabet)
        => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException?.GetType().Name == "PostgresException"
           && (ex.InnerException as dynamic)?.SqlState == "23505";
}

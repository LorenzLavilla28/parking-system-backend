using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Users;

/// <summary>
/// A staff member (platform admin, tenant admin, supervisor, or guard).
/// Platform administrators carry a sentinel <see cref="TenantId"/> of
/// <see cref="Guid.Empty"/> because they belong to no tenant.
/// </summary>
public class ApplicationUser : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool MustChangePassword { get; private set; }
    public UserStatus Status { get; private set; } = UserStatus.Active;

    // Account lockout bookkeeping.
    public int FailedLoginAttempts { get; private set; }
    public DateTimeOffset? LockedOutUntil { get; private set; }

    private readonly List<UserRole> _roles = new();
    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    private readonly List<UserParkingLocation> _locationAssignments = new();
    public IReadOnlyCollection<UserParkingLocation> LocationAssignments => _locationAssignments.AsReadOnly();

    private ApplicationUser() { }

    public ApplicationUser(Guid tenantId, string firstName, string lastName, string email, string passwordHash, bool mustChangePassword = false)
    {
        TenantId = tenantId;
        FirstName = (firstName ?? string.Empty).Trim();
        LastName = (lastName ?? string.Empty).Trim();
        SetEmail(email);
        PasswordHash = passwordHash ?? throw new ArgumentNullException(nameof(passwordHash));
        MustChangePassword = mustChangePassword;
        Status = UserStatus.Active;
    }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public void SetEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("user.email_required", "Email is required.");
        Email = email.Trim().ToLowerInvariant();
    }

    public void SetPasswordHash(string passwordHash)
        => PasswordHash = passwordHash ?? throw new ArgumentNullException(nameof(passwordHash));

    public void CompletePasswordChange(string passwordHash)
    {
        SetPasswordHash(passwordHash);
        MustChangePassword = false;
    }

    public void AddRole(RoleType role)
    {
        if (_roles.All(r => r.Role != role))
            _roles.Add(new UserRole(Id, role));
    }

    public void RemoveRole(RoleType role)
        => _roles.RemoveAll(r => r.Role == role);

    public void AssignLocation(Guid parkingLocationId)
    {
        if (_locationAssignments.All(a => a.ParkingLocationId != parkingLocationId))
            _locationAssignments.Add(new UserParkingLocation(Id, parkingLocationId, TenantId));
    }

    public void UnassignLocation(Guid parkingLocationId)
        => _locationAssignments.RemoveAll(a => a.ParkingLocationId == parkingLocationId);

    public bool HasRole(RoleType role) => _roles.Any(r => r.Role == role);

    public void Disable() => Status = UserStatus.Disabled;
    public void Enable()
    {
        Status = UserStatus.Active;
        FailedLoginAttempts = 0;
        LockedOutUntil = null;
    }

    /// <summary>
    /// True while a temporary lockout window is in effect, or the account has
    /// been hard-locked by an administrator. A temporary lockout auto-expires
    /// once <see cref="LockedOutUntil"/> passes.
    /// </summary>
    public bool IsLockedOut(DateTimeOffset now)
        => Status == UserStatus.Locked || (LockedOutUntil.HasValue && LockedOutUntil.Value > now);

    /// <summary>
    /// Records a failed sign-in. After the threshold the account is locked out
    /// for <paramref name="lockoutDuration"/> via a timer; <see cref="Status"/>
    /// is left untouched so the lockout lifts automatically when the window ends.
    /// </summary>
    public void RegisterFailedLogin(DateTimeOffset now, int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= maxAttempts)
            LockedOutUntil = now.Add(lockoutDuration);
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockedOutUntil = null;
    }

    public bool CanAuthenticate => Status == UserStatus.Active;
}

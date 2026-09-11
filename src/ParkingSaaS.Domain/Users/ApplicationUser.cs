using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Users;

/// <summary>
/// A global login identity. Access is granted through scoped memberships, while
/// TenantId is retained as the legacy/default tenant for compatibility with
/// existing records and APIs.
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

    private readonly List<UserMembership> _memberships = new();
    public IReadOnlyCollection<UserMembership> Memberships => _memberships.AsReadOnly();

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
        EnsureMembership(tenantId);
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

    public UserMembership EnsureMembership(Guid tenantId)
    {
        var membership = _memberships.FirstOrDefault(m => m.TenantId == tenantId);
        if (membership is not null) return membership;

        membership = new UserMembership(Id, tenantId);
        _memberships.Add(membership);
        return membership;
    }

    public UserMembership? GetMembership(Guid tenantId)
        => _memberships.FirstOrDefault(m => m.TenantId == tenantId);

    public void AddRole(RoleType role, Guid? tenantId = null)
    {
        var scope = tenantId ?? TenantId;
        EnsureMembership(scope);
        if (_roles.All(r => r.Role != role || r.TenantId != scope))
            _roles.Add(new UserRole(Id, role, scope));
    }

    public void RemoveRole(RoleType role, Guid? tenantId = null)
    {
        var scope = tenantId ?? TenantId;
        _roles.RemoveAll(r => r.Role == role && r.TenantId == scope);
    }

    public void AssignLocation(Guid parkingLocationId, Guid? tenantId = null)
    {
        var scope = tenantId ?? TenantId;
        EnsureMembership(scope);
        if (_locationAssignments.All(a => a.ParkingLocationId != parkingLocationId || a.TenantId != scope))
            _locationAssignments.Add(new UserParkingLocation(Id, parkingLocationId, scope));
    }

    public void UnassignLocation(Guid parkingLocationId, Guid? tenantId = null)
    {
        var scope = tenantId ?? TenantId;
        _locationAssignments.RemoveAll(a => a.ParkingLocationId == parkingLocationId && a.TenantId == scope);
    }

    public bool HasRole(RoleType role, Guid? tenantId = null)
    {
        var scope = tenantId ?? TenantId;
        return _roles.Any(r => r.Role == role && r.TenantId == scope);
    }

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

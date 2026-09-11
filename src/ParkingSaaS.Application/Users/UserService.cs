using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Users;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Application.Users;

/// <summary>
/// Tenant-scoped management of staff (tenant admins, supervisors, guards).
/// A tenant administrator cannot create platform administrators, and location
/// assignments are validated against locations the tenant actually owns.
/// </summary>
public sealed class UserService : IUserService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailQueue _emailQueue;
    private readonly IDateTime _clock;

    public UserService(IApplicationDbContext db, ITenantContext tenant, IPasswordHasher passwordHasher, IEmailQueue emailQueue, IDateTime clock)
    {
        _db = db;
        _tenant = tenant;
        _passwordHasher = passwordHasher;
        _emailQueue = emailQueue;
        _clock = clock;
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var roles = ParseRoles(request.Roles);
        var locationIds = await ValidateLocationsAsync(request.AssignedLocationIds, ct);

        var existing = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .Include(u => u.Memberships)
            .Include(u => u.LocationAssignments)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (existing is not null)
        {
            if (!existing.CanAuthenticate)
                throw new ConflictException("This account is disabled and cannot receive tenant access.");

            var membership = existing.Memberships.FirstOrDefault(m => m.TenantId == _tenant.TenantId);
            var newMembership = membership is null;
            var alreadyHasTenantRole = existing.Roles.Any(r => r.TenantId == _tenant.TenantId);
            if (alreadyHasTenantRole && membership?.IsActive == true)
                throw new ConflictException("This account already has access to this tenant.");

            membership ??= new UserMembership(existing.Id, _tenant.TenantId);
            membership.Enable();
            if (newMembership) _db.UserMemberships.Add(membership);

            foreach (var role in roles)
            {
                if (existing.Roles.All(r => r.TenantId != _tenant.TenantId || r.Role != role))
                    _db.UserRoles.Add(new UserRole(existing.Id, role, _tenant.TenantId));
            }

            foreach (var locationId in locationIds)
            {
                if (existing.LocationAssignments.All(a => a.TenantId != _tenant.TenantId || a.ParkingLocationId != locationId))
                    _db.UserParkingLocations.Add(new UserParkingLocation(existing.Id, locationId, _tenant.TenantId));
            }

            var existingTenantName = await _db.Tenants
                .Where(t => t.Id == _tenant.TenantId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(ct) ?? "your organization";
            _emailQueue.QueueTenantAccessGranted(
                _tenant.TenantId, existing.Email, existing.FullName, existingTenantName,
                roles.Select(RoleNames.ToName).ToArray(), _clock.UtcNow);
            await _db.SaveChangesAsync(ct);
            return ToResponse(existing, _tenant.TenantId);
        }

        var user = new ApplicationUser(
            _tenant.TenantId,
            request.FirstName,
            request.LastName,
            email,
            _passwordHasher.Hash(request.Password),
            mustChangePassword: true);

        foreach (var role in roles) user.AddRole(role, _tenant.TenantId);
        foreach (var locationId in locationIds) user.AssignLocation(locationId, _tenant.TenantId);

        await _db.Users.AddAsync(user, ct);

        // Notify the new staff member (queued in the same transaction as the account).
        var tenantName = await _db.Tenants
            .Where(t => t.Id == _tenant.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct) ?? "your organization";
        _emailQueue.QueueUserWelcome(
            _tenant.TenantId, email, $"{request.FirstName} {request.LastName}".Trim(),
            tenantName, roles.Select(RoleNames.ToName).ToArray(), request.Password, _clock.UtcNow);

        await _db.SaveChangesAsync(ct);
        return ToResponse(user, _tenant.TenantId);
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        UserResponse? response = null;

        try
        {
            // User updates rebuild role and location collections. Lock the aggregate
            // before loading it so two stale admin forms cannot both mutate the same
            // child row and make EF report a zero-row update/delete.
            await _db.ExecuteInTransactionAsync(async txct =>
            {
                await _db.LockUserAsync(id, txct);

                var user = await _db.Users
                    .Include(u => u.Roles)
                    .Include(u => u.Memberships)
                    .Include(u => u.LocationAssignments)
                    .FirstOrDefaultAsync(u => u.Id == id, txct)
                    ?? throw new NotFoundException("User not found.");

                // Rebuild role set.
                var desiredRoles = ParseRoles(request.Roles);
                await EnsureTenantAdministratorRemainsAsync(user, desiredRoles, request.IsActive, txct);
                foreach (var existing in user.Roles.Select(r => r.Role).ToArray())
                    if (!desiredRoles.Contains(existing)) user.RemoveRole(existing, _tenant.TenantId);
                foreach (var role in desiredRoles) user.AddRole(role, _tenant.TenantId);

                // Rebuild location assignments.
                var desiredLocations = await ValidateLocationsAsync(request.AssignedLocationIds, txct);
                var existingLocationIds = user.LocationAssignments
                    .Select(a => a.ParkingLocationId)
                    .ToHashSet();

                foreach (var existing in user.LocationAssignments.Select(a => a.ParkingLocationId).ToArray())
                    if (!desiredLocations.Contains(existing)) user.UnassignLocation(existing, _tenant.TenantId);

                foreach (var locationId in desiredLocations) user.AssignLocation(locationId, _tenant.TenantId);

                // ApplicationUser creates GUID keys in the domain. Because those
                // keys are non-default, EF can infer Modified instead of Added
                // when a new child is introduced through the navigation alone.
                // Explicitly add only the new join rows so PostgreSQL performs an
                // INSERT rather than an UPDATE that affects zero rows.
                foreach (var assignment in user.LocationAssignments
                             .Where(a => !existingLocationIds.Contains(a.ParkingLocationId)))
                {
                    _db.UserParkingLocations.Add(assignment);
                }

                var membership = user.Memberships.FirstOrDefault(m => m.TenantId == _tenant.TenantId)
                    ?? throw new NotFoundException("User membership not found.");
                if (request.IsActive)
                {
                    membership.Enable();
                }
                else
                {
                    membership.Disable();

                    // Access tokens are checked by the tenant middleware on
                    // every request, but refresh tokens are otherwise valid
                    // until expiry. Revoke this tenant's refresh sessions so
                    // re-enabling the membership cannot resurrect an old
                    // credential.
                    var now = _clock.UtcNow;
                    var refreshTokens = await _db.RefreshTokens
                        .IgnoreQueryFilters()
                        .Where(token => token.UserId == user.Id
                            && token.TenantId == _tenant.TenantId
                            && token.RevokedAt == null)
                        .ToListAsync(txct);
                    foreach (var refreshToken in refreshTokens)
                        refreshToken.Revoke(now);
                }

                await _db.SaveChangesAsync(txct);
                response = ToResponse(user, _tenant.TenantId);
            }, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "This user was changed by another request. Refresh the user and try again.");
        }

        return response ?? throw new InvalidOperationException("The user update did not produce a response.");
    }

    private async Task EnsureTenantAdministratorRemainsAsync(
        ApplicationUser user,
        IReadOnlyCollection<RoleType> desiredRoles,
        bool remainsActive,
        CancellationToken ct)
    {
        var membership = user.Memberships.FirstOrDefault(m => m.TenantId == _tenant.TenantId);
        var isCurrentAdministrator = user.Roles.Any(r =>
            r.TenantId == _tenant.TenantId && r.Role == RoleType.TenantAdministrator);

        if (membership?.IsActive != true
            || !isCurrentAdministrator
            || (remainsActive && desiredRoles.Contains(RoleType.TenantAdministrator)))
            return;

        var anotherActiveAdministratorExists = await _db.Users
            .Where(candidate => candidate.Id != user.Id)
            .AnyAsync(candidate =>
                candidate.Status == UserStatus.Active
                && candidate.Memberships.Any(m =>
                    m.TenantId == _tenant.TenantId && m.Status == MembershipStatus.Active)
                && candidate.Roles.Any(r =>
                    r.TenantId == _tenant.TenantId && r.Role == RoleType.TenantAdministrator),
                ct);

        if (!anotherActiveAdministratorExists)
            throw new ConflictException(
                "At least one active tenant administrator must remain.");
    }

    public async Task<UserResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.Roles)
            .Include(u => u.Memberships)
            .Include(u => u.LocationAssignments)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("User not found.");
        return ToResponse(user, _tenant.TenantId);
    }

    public async Task<PagedResult<UserResponse>> ListAsync(PageQuery query, CancellationToken ct)
    {
        var q = _db.Users
            .Include(u => u.Roles)
            .Include(u => u.Memberships)
            .Include(u => u.LocationAssignments)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            q = q.Where(u => u.Email.Contains(term) || u.FirstName.ToLower().Contains(term) || u.LastName.ToLower().Contains(term));
        }

        q = query.Sort?.ToLowerInvariant() switch
        {
            "email" => q.OrderBy(u => u.Email),
            "-created" => q.OrderByDescending(u => u.CreatedAt),
            _ => q.OrderBy(u => u.CreatedAt)
        };

        var total = await q.LongCountAsync(ct);
        var items = await q
            .Skip((query.NormalizedPage - 1) * query.NormalizedPageSize)
            .Take(query.NormalizedPageSize)
            .ToListAsync(ct);

        return new PagedResult<UserResponse>(
            items.Select(u => ToResponse(u, _tenant.TenantId)).ToArray(),
            query.NormalizedPage,
            query.NormalizedPageSize,
            total);
    }

    private static IReadOnlyCollection<RoleType> ParseRoles(IReadOnlyCollection<string> roleNames)
    {
        if (roleNames is null || roleNames.Count == 0)
            throw new ConflictException("At least one role is required.");

        var roles = new List<RoleType>();
        foreach (var name in roleNames)
        {
            if (!RoleNames.TryParse(name, out var role))
                throw new ConflictException($"Unknown role '{name}'.");
            if (role == RoleType.PlatformAdministrator)
                throw new ForbiddenException("Tenant users cannot be platform administrators.");
            roles.Add(role);
        }
        return roles;
    }

    private async Task<IReadOnlyCollection<Guid>> ValidateLocationsAsync(IReadOnlyCollection<Guid>? ids, CancellationToken ct)
    {
        if (ids is null || ids.Count == 0) return Array.Empty<Guid>();

        var distinct = ids.Distinct().ToArray();
        // Tenant filter guarantees we only match this tenant's locations.
        var ownedCount = await _db.ParkingLocations.CountAsync(l => distinct.Contains(l.Id), ct);
        if (ownedCount != distinct.Length)
            throw new ConflictException("One or more assigned locations do not belong to this tenant.");
        return distinct;
    }

    private static UserResponse ToResponse(ApplicationUser u, Guid tenantId) => new(
        u.Id,
        tenantId,
        u.FirstName,
        u.LastName,
        u.Email,
        u.Memberships.FirstOrDefault(m => m.TenantId == tenantId)?.Status.ToString() ?? u.Status.ToString(),
        u.Roles.Where(r => r.TenantId == tenantId).Select(r => RoleNames.ToName(r.Role)).ToArray(),
        u.LocationAssignments.Where(a => a.TenantId == tenantId).Select(a => a.ParkingLocationId).ToArray(),
        u.CreatedAt);
}

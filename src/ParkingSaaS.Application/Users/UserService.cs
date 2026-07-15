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
        var emailTaken = await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct);
        if (emailTaken)
            throw new ConflictException("A user with this email already exists.");

        var roles = ParseRoles(request.Roles);
        var locationIds = await ValidateLocationsAsync(request.AssignedLocationIds, ct);

        var user = new ApplicationUser(
            _tenant.TenantId,
            request.FirstName,
            request.LastName,
            email,
            _passwordHasher.Hash(request.Password),
            mustChangePassword: true);

        foreach (var role in roles) user.AddRole(role);
        foreach (var locationId in locationIds) user.AssignLocation(locationId);

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
        return ToResponse(user);
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.Roles)
            .Include(u => u.LocationAssignments)
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("User not found.");

        // Rebuild role set.
        var desiredRoles = ParseRoles(request.Roles);
        foreach (var existing in user.Roles.Select(r => r.Role).ToArray())
            if (!desiredRoles.Contains(existing)) user.RemoveRole(existing);
        foreach (var role in desiredRoles) user.AddRole(role);

        // Rebuild location assignments.
        var desiredLocations = await ValidateLocationsAsync(request.AssignedLocationIds, ct);
        foreach (var existing in user.LocationAssignments.Select(a => a.ParkingLocationId).ToArray())
            if (!desiredLocations.Contains(existing)) user.UnassignLocation(existing);
        foreach (var locationId in desiredLocations) user.AssignLocation(locationId);

        if (request.IsActive) user.Enable(); else user.Disable();

        await _db.SaveChangesAsync(ct);
        return ToResponse(user);
    }

    public async Task<UserResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(u => u.Roles)
            .Include(u => u.LocationAssignments)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("User not found.");
        return ToResponse(user);
    }

    public async Task<PagedResult<UserResponse>> ListAsync(PageQuery query, CancellationToken ct)
    {
        var q = _db.Users
            .Include(u => u.Roles)
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
            items.Select(ToResponse).ToArray(),
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

    private static UserResponse ToResponse(ApplicationUser u) => new(
        u.Id,
        u.TenantId,
        u.FirstName,
        u.LastName,
        u.Email,
        u.Status.ToString(),
        u.Roles.Select(r => RoleNames.ToName(r.Role)).ToArray(),
        u.LocationAssignments.Select(a => a.ParkingLocationId).ToArray(),
        u.CreatedAt);
}

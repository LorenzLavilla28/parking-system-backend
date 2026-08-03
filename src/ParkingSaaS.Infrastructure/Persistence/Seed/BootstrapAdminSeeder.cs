using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Infrastructure.Persistence.Seed;

public sealed class BootstrapAdminSeeder
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<BootstrapAdminSeeder> _logger;

    public BootstrapAdminSeeder(
        AppDbContext db,
        IPasswordHasher hasher,
        ILogger<BootstrapAdminSeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task SeedAsync(BootstrapAdminOptions options, CancellationToken ct = default)
    {
        if (!options.Enabled)
            return;

        if (string.IsNullOrWhiteSpace(options.Email))
            throw new InvalidOperationException("BootstrapAdmin:Email is required when bootstrap is enabled.");
        if (string.IsNullOrWhiteSpace(options.Password) || options.Password.Length < 12)
            throw new InvalidOperationException("BootstrapAdmin:Password must contain at least 12 characters when bootstrap is enabled.");

        var platformAdminExists = await _db.UserRoles
            .AnyAsync(role => role.Role == RoleType.PlatformAdministrator, ct);
        if (platformAdminExists)
        {
            _logger.LogInformation("A platform administrator already exists; bootstrap was skipped.");
            return;
        }

        var admin = new ApplicationUser(
            Guid.Empty,
            options.FirstName,
            options.LastName,
            options.Email,
            _hasher.Hash(options.Password),
            mustChangePassword: true);
        admin.AddRole(RoleType.PlatformAdministrator);

        await _db.Users.AddAsync(admin, ct);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created bootstrap platform administrator {Email}.", admin.Email);
    }
}

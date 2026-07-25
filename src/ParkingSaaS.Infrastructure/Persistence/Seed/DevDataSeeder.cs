using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds only the platform administrator for local development. Never invoked
/// in production. Credentials here are a well-known dev password.
/// </summary>
public sealed class DevDataSeeder
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<DevDataSeeder> _logger;

    public DevDataSeeder(AppDbContext db, IPasswordHasher hasher, ILogger<DevDataSeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        // Platform administrator (tenant-less).
        if (!await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == "platform@parking.local", ct))
        {
            var platformAdmin = new ApplicationUser(Guid.Empty, "Platform", "Admin",
                "platform@parking.local", _hasher.Hash("Platform!2026"));
            platformAdmin.AddRole(RoleType.PlatformAdministrator);
            await _db.Users.AddAsync(platformAdmin, ct);
            _logger.LogInformation("Seeded platform administrator platform@parking.local");
        }

        await _db.SaveChangesAsync(ct);
    }
}

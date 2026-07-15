using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Tenants;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds a minimal, deterministic data set for local development only. Never
/// invoked in production. Credentials here are well-known dev passwords.
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

        // Demo tenant with an admin, a supervisor, a guard, and one location.
        if (!await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Slug == "demo", ct))
        {
            var tenant = new Tenant("Demo Parking Co", "demo", SubscriptionPlan.Growth, "PHP", "Asia/Manila");
            await _db.Tenants.AddAsync(tenant, ct);

            var location = new ParkingLocation(tenant.Id, "Demo Mall Parking", "demo-mall", "Asia/Manila", "123 Demo Ave");
            await _db.ParkingLocations.AddAsync(location, ct);

            var admin = new ApplicationUser(tenant.Id, "Demo", "Admin", "admin@demo.local", _hasher.Hash("Admin!2026"));
            admin.AddRole(RoleType.TenantAdministrator);

            var supervisor = new ApplicationUser(tenant.Id, "Demo", "Supervisor", "supervisor@demo.local", _hasher.Hash("Super!2026"));
            supervisor.AddRole(RoleType.Supervisor);
            supervisor.AssignLocation(location.Id);

            var guard = new ApplicationUser(tenant.Id, "Demo", "Guard", "guard@demo.local", _hasher.Hash("Guard!2026"));
            guard.AddRole(RoleType.Guard);
            guard.AssignLocation(location.Id);

            await _db.Users.AddRangeAsync(new[] { admin, supervisor, guard }, ct);
            _logger.LogInformation("Seeded demo tenant with admin/supervisor/guard and one location");
        }

        await _db.SaveChangesAsync(ct);
    }
}

using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Domain.Audit;
using ParkingSaaS.Domain.Emails;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.RatePlans;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Tenants;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Application.Abstractions;

/// <summary>
/// The persistence seam exposed to the application layer. Concrete EF Core
/// configuration, global tenant filters, and interceptors live in
/// infrastructure; handlers depend only on this surface.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<ApplicationUser> Users { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<UserParkingLocation> UserParkingLocations { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<ParkingLocation> ParkingLocations { get; }
    DbSet<ParkingSession> ParkingSessions { get; }
    DbSet<RatePlan> RatePlans { get; }
    DbSet<RatePlanVersion> RatePlanVersions { get; }
    DbSet<FeeQuote> FeeQuotes { get; }
    DbSet<Payment> Payments { get; }
    DbSet<TenantPayMongoConnection> TenantPayMongoConnections { get; }
    DbSet<WebhookEvent> WebhookEvents { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<EmailMessage> Emails { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
    Task LockLocationAsync(Guid locationId, CancellationToken cancellationToken = default);
    Task LockTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

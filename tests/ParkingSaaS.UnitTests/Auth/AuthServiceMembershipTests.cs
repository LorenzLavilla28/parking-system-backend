using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Auth;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Contracts.Auth;
using ParkingSaaS.Domain.Tenants;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Infrastructure.Identity;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Auth;

public sealed class AuthServiceMembershipTests
{
    [Fact]
    public async Task Dual_access_account_can_switch_contexts_and_disabled_tenant_membership_cannot_refresh()
    {
        var tenantContext = new MutableTenantContext();
        tenantContext.ScopeToPlatformAdmin();
        await using var db = InMemoryDb.Create(tenantContext);
        var clock = new TestClock(new DateTimeOffset(2026, 9, 11, 16, 0, 0, TimeSpan.Zero));
        var tenant = new Tenant("Acme", "acme", SubscriptionPlan.Growth, "PHP", "Asia/Manila");
        var user = new ApplicationUser(
            tenant.Id,
            "Alex",
            "Admin",
            "alex@acme.test",
            new PasswordHasher().Hash("StrongPass!2026"));
        user.AddRole(RoleType.TenantAdministrator, tenant.Id);
        user.AddRole(RoleType.PlatformAdministrator, Guid.Empty);
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = CreateService(db, clock);

        var login = await service.LoginAsync(
            new LoginRequest(user.Email, "StrongPass!2026"),
            null,
            CancellationToken.None);

        login.User.TenantId.Should().Be(Guid.Empty);
        login.User.Roles.Should().Equal(nameof(RoleType.PlatformAdministrator));
        login.User.AvailableContexts.Should().HaveCount(2);

        var tenantSession = await service.SwitchContextAsync(
            new SwitchContextRequest(tenant.Id),
            user.Id,
            null,
            CancellationToken.None);

        tenantSession.User.TenantId.Should().Be(tenant.Id);
        tenantSession.User.Roles.Should().Equal(nameof(RoleType.TenantAdministrator));

        user.GetMembership(tenant.Id)!.Disable();
        await db.SaveChangesAsync();

        var platformSession = await service.SwitchContextAsync(
            new SwitchContextRequest(Guid.Empty),
            user.Id,
            null,
            CancellationToken.None);
        platformSession.User.TenantId.Should().Be(Guid.Empty);

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            service.SwitchContextAsync(
                new SwitchContextRequest(tenant.Id),
                user.Id,
                null,
                CancellationToken.None));

        await Assert.ThrowsAsync<UnauthorizedAppException>(() =>
            service.RefreshAsync(
                new RefreshRequest(tenantSession.RefreshToken),
                null,
                CancellationToken.None));
    }

    [Fact]
    public async Task Login_skips_a_suspended_legacy_tenant_when_another_membership_is_active()
    {
        var tenantContext = new MutableTenantContext();
        tenantContext.ScopeToPlatformAdmin();
        await using var db = InMemoryDb.Create(tenantContext);
        var clock = new TestClock(new DateTimeOffset(2026, 9, 11, 16, 0, 0, TimeSpan.Zero));
        var suspendedTenant = new Tenant("Old tenant", "old-tenant", SubscriptionPlan.Growth, "PHP", "Asia/Manila");
        var activeTenant = new Tenant("Current tenant", "current-tenant", SubscriptionPlan.Growth, "PHP", "Asia/Manila");
        suspendedTenant.ChangeStatus(TenantStatus.Suspended);
        var user = new ApplicationUser(
            suspendedTenant.Id,
            "Casey",
            "Operator",
            "casey@example.test",
            new PasswordHasher().Hash("StrongPass!2026"));
        user.EnsureMembership(activeTenant.Id);
        user.AddRole(RoleType.TenantAdministrator, suspendedTenant.Id);
        user.AddRole(RoleType.TenantAdministrator, activeTenant.Id);
        db.Tenants.AddRange(suspendedTenant, activeTenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var response = await CreateService(db, clock).LoginAsync(
            new LoginRequest(user.Email, "StrongPass!2026"),
            null,
            CancellationToken.None);

        response.User.TenantId.Should().Be(activeTenant.Id);
        response.User.AvailableContexts.Should().ContainSingle();
        response.User.AvailableContexts.Single().TenantId.Should().Be(activeTenant.Id);
    }

    private static AuthService CreateService(
        ParkingSaaS.Infrastructure.Persistence.AppDbContext db,
        TestClock clock)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "ParkingSaaS",
            Audience = "ParkingSaaS",
            SigningKey = "unit-test-signing-key-at-least-32-characters-long!!",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 14,
        });

        return new AuthService(
            db,
            new PasswordHasher(),
            new JwtTokenService(jwtOptions, clock),
            new RefreshTokenService(),
            TestEmail.Queue(db),
            clock,
            Options.Create(new LockoutOptions()),
            jwtOptions,
            Options.Create(new PasswordResetOptions()),
            NullLogger<AuthService>.Instance);
    }
}

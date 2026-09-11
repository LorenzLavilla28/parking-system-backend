using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Api.Middleware;
using ParkingSaaS.Application.Common;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Domain.Tenants;
using ParkingSaaS.Domain.Users;
using ParkingSaaS.Infrastructure.Identity;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;

namespace ParkingSaaS.UnitTests.Api;

public sealed class TenantStatusMiddlewareTests
{
    [Fact]
    public async Task Suspended_tenant_is_blocked_before_controller_runs()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, TenantStatus.Suspended);
        var nextCalled = false;
        var middleware = new TenantStatusMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext(db.Tenants.Single().Id, "/api/tenant/payments");

        var act = () => middleware.InvokeAsync(context, db);

        await act.Should().ThrowAsync<TenantSuspendedException>();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Active_tenant_is_allowed_to_continue()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, TenantStatus.Active);
        var nextCalled = false;
        var middleware = new TenantStatusMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(CreateContext(db.Tenants.Single().Id, "/api/tenant/payments"), db);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Disabled_membership_is_blocked_even_when_the_tenant_is_active()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, TenantStatus.Active);
        var user = new ApplicationUser(
            tenantId,
            "Disabled",
            "Member",
            "disabled-member@example.test",
            "hash");
        user.AddRole(RoleType.TenantAdministrator, tenantId);
        user.GetMembership(tenantId)!.Disable();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var nextCalled = false;
        var middleware = new TenantStatusMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var act = () => middleware.InvokeAsync(
            CreateContext(db.Tenants.Single().Id, "/api/tenant/payments", user.Id),
            db);

        await act.Should().ThrowAsync<UnauthorizedAppException>();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Platform_admin_can_access_tenant_management_for_suspended_tenant()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, TenantStatus.Suspended);
        var nextCalled = false;
        var middleware = new TenantStatusMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = CreateContext(db.Tenants.Single().Id, "/api/platform/tenants");
        context.User.AddIdentity(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, RoleNames.PlatformAdministrator),
        }, "test"));

        await middleware.InvokeAsync(context, db);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Existing_customer_payment_paths_are_not_blocked_by_staff_suspension_guard()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId, TenantStatus.Suspended);
        var nextCalled = false;
        var middleware = new TenantStatusMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(CreateContext(db.Tenants.Single().Id, "/api/customer/sessions/public-token"), db);

        nextCalled.Should().BeTrue();
    }

    private static AppDbContext CreateDb(Guid tenantId, TenantStatus status)
    {
        var tenant = new MutableTenantContext();
        tenant.ScopeTo(tenantId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options, tenant);
        db.Tenants.Add(new Tenant("Demo", $"demo-{tenantId:N}", SubscriptionPlan.Starter, "PHP", "Asia/Manila"));
        db.SaveChanges();
        db.Tenants.Single().ChangeStatus(status);
        db.SaveChanges();
        return db;
    }

    private static DefaultHttpContext CreateContext(Guid tenantId, string path, Guid? userId = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        var claims = new List<Claim>
        {
            new(AppClaimTypes.TenantId, tenantId.ToString()),
            new(ClaimTypes.Role, RoleNames.TenantAdministrator),
        };
        if (userId.HasValue)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return context;
    }
}

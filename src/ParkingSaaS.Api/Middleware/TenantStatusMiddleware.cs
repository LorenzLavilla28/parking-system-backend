using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Common;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Domain.Tenants;
using ParkingSaaS.Infrastructure.Identity;
using ParkingSaaS.Infrastructure.Persistence;

namespace ParkingSaaS.Api.Middleware;

/// <summary>
/// Enforces tenant lifecycle status for every authenticated tenant request.
/// This is deliberately centralized so newly-added controllers cannot forget
/// to check whether the tenant is still allowed to operate.
/// </summary>
public sealed class TenantStatusMiddleware
{
    private readonly RequestDelegate _next;

    public TenantStatusMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var principal = context.User;

        // Anonymous customer, webhook, health, and auth endpoints have their own
        // rules. Platform administrators are never owned by a tenant and must be
        // able to reactivate a suspended tenant.
        if (principal.Identity?.IsAuthenticated == true
            && !principal.IsInRole(RoleNames.PlatformAdministrator)
            && !IsPublicOrAuthenticationPath(context.Request.Path)
            && Guid.TryParse(principal.FindFirstValue(AppClaimTypes.TenantId), out var tenantId)
            && tenantId != Guid.Empty)
        {
            var status = await db.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => (TenantStatus?)t.Status)
                .SingleOrDefaultAsync(context.RequestAborted);

            if (status != TenantStatus.Active)
                throw new TenantSuspendedException(
                    status == TenantStatus.Archived
                        ? "This tenant membership is archived. Contact your platform administrator."
                        : "This tenant membership is suspended. Contact your platform administrator.");
        }

        await _next(context);
    }

    private static bool IsPublicOrAuthenticationPath(PathString path)
        => path.StartsWithSegments("/api/auth")
           || path.StartsWithSegments("/api/customer")
           || path.StartsWithSegments("/api/payments/webhooks")
           || path.StartsWithSegments("/api/health");
}

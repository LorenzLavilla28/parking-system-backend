using Microsoft.AspNetCore.Authorization;
using ParkingSaaS.Application.Common;

namespace ParkingSaaS.Api.Auth;

/// <summary>
/// Named authorization policies. Platform administration is intentionally a
/// separate policy from any tenant role so the two authorization surfaces never
/// overlap.
/// </summary>
public static class AuthorizationPolicies
{
    public const string PlatformAdmin = "PlatformAdmin";
    public const string TenantAdmin = "TenantAdmin";
    public const string SupervisorOrAbove = "SupervisorOrAbove";
    public const string GuardOrAbove = "GuardOrAbove";

    public static AuthorizationOptions AddParkingPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(PlatformAdmin, p => p.RequireRole(RoleNames.PlatformAdministrator).RequireClaim("password_changed", "true"));
        options.AddPolicy(TenantAdmin, p => p.RequireRole(RoleNames.TenantAdministrator).RequireClaim("password_changed", "true"));
        options.AddPolicy(SupervisorOrAbove, p => p.RequireRole(RoleNames.TenantAdministrator, RoleNames.Supervisor).RequireClaim("password_changed", "true"));
        options.AddPolicy(GuardOrAbove, p => p.RequireRole(RoleNames.TenantAdministrator, RoleNames.Supervisor, RoleNames.Guard).RequireClaim("password_changed", "true"));
        return options;
    }
}

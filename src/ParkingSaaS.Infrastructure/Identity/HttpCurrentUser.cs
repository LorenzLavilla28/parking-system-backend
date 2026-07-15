using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Infrastructure.Identity;

public sealed class HttpCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; }
    public Guid? UserId { get; }
    public Guid TenantId { get; }
    public IReadOnlyCollection<RoleType> Roles { get; }

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            Roles = Array.Empty<RoleType>();
            return;
        }

        IsAuthenticated = true;

        if (Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            UserId = userId;

        if (Guid.TryParse(principal.FindFirstValue(AppClaimTypes.TenantId), out var tenantId))
            TenantId = tenantId;

        Roles = principal.FindAll(ClaimTypes.Role)
            .Select(c => RoleNames.TryParse(c.Value, out var r) ? (RoleType?)r : null)
            .Where(r => r.HasValue)
            .Select(r => r!.Value)
            .ToArray();
    }
}

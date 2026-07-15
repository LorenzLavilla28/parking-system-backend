using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Application.Abstractions;

/// <summary>Identity of the authenticated staff principal for the current request.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid TenantId { get; }
    IReadOnlyCollection<RoleType> Roles { get; }
}

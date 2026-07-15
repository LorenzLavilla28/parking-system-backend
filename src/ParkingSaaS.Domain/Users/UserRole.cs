using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Users;

/// <summary>Join entity assigning a <see cref="RoleType"/> to a user.</summary>
public class UserRole : Entity
{
    public Guid UserId { get; private set; }
    public RoleType Role { get; private set; }

    private UserRole() { }

    public UserRole(Guid userId, RoleType role)
    {
        UserId = userId;
        Role = role;
    }
}

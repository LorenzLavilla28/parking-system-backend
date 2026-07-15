using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Application.Common;

/// <summary>Canonical role name constants and parsing used across auth and policies.</summary>
public static class RoleNames
{
    public const string PlatformAdministrator = nameof(RoleType.PlatformAdministrator);
    public const string TenantAdministrator = nameof(RoleType.TenantAdministrator);
    public const string Supervisor = nameof(RoleType.Supervisor);
    public const string Guard = nameof(RoleType.Guard);

    public static string ToName(RoleType role) => role.ToString();

    public static bool TryParse(string? value, out RoleType role)
        => Enum.TryParse(value, ignoreCase: true, out role) && Enum.IsDefined(role);
}

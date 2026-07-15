namespace ParkingSaaS.Infrastructure.Identity;

/// <summary>Custom JWT claim names used across token issuance and reading.</summary>
public static class AppClaimTypes
{
    public const string TenantId = "tenant_id";
    public const string LocationId = "location_id";
    public const string Role = "role";
    public const string PasswordChanged = "password_changed";
}

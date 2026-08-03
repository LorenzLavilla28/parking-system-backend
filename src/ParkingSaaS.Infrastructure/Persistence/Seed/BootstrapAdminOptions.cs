namespace ParkingSaaS.Infrastructure.Persistence.Seed;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";

    public bool Enabled { get; set; }
    public string FirstName { get; set; } = "Platform";
    public string LastName { get; set; } = "Administrator";
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

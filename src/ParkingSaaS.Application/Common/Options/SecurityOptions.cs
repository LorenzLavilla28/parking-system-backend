namespace ParkingSaaS.Application.Common.Options;

/// <summary>JWT and account-lockout policy. Bound from configuration / secrets.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "ParkingSaaS";
    public string Audience { get; set; } = "ParkingSaaS";

    /// <summary>HMAC signing key. Supplied via Secrets Manager/SSM in non-dev environments.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
}

public sealed class LockoutOptions
{
    public const string SectionName = "Lockout";

    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    public int TokenLifetimeMinutes { get; set; } = 60;
}

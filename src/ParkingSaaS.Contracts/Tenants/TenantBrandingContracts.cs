namespace ParkingSaaS.Contracts.Tenants;

public sealed record TenantBrandingResponse(
    string? LogoUrl,
    string? ContentType,
    long MaxLogoBytes);

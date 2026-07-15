namespace ParkingSaaS.Contracts.Tenants;

public sealed record CreateTenantRequest(
    string Name,
    string Slug,
    string SubscriptionPlan,
    string DefaultCurrency,
    string DefaultTimezone,
    string AdminFirstName,
    string AdminLastName,
    string AdminEmail,
    string AdminPassword,
    CreateTenantLocationRequest? FirstLocation = null);

public sealed record CreateTenantLocationRequest(
    string Name,
    string Slug,
    string? Address,
    string Timezone,
    int ExitGraceMinutes,
    bool AllowCashPayment);

public sealed record UpdateTenantStatusRequest(string Status);

public sealed record TenantResponse(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    string SubscriptionPlan,
    string DefaultCurrency,
    string DefaultTimezone,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    TenantOnboardingLocationResponse? FirstLocation = null);

public sealed record TenantOnboardingLocationResponse(
    Guid Id,
    string Name,
    string Slug);

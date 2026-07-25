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
    string AdminPassword);

public sealed record CreateTenantAddOnLocationRequest(
    string Name,
    string Slug,
    string? Address,
    string Timezone,
    int SlotCapacity,
    bool AllowCashPayment = true,
    decimal? MonthlyPrice = null);

public sealed record UpdateTenantStatusRequest(string Status);

public sealed record TenantResponse(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    string SubscriptionPlan,
    string DefaultCurrency,
    string DefaultTimezone,
    int? MaximumLocations,
    int? MaximumSlotsPerLocation,
    decimal? MonthlyPrice,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

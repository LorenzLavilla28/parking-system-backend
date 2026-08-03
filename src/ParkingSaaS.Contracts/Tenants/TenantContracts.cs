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
    int? PurchasedSlotCapacityPerLocation = null);

public sealed record UpdateTenantStatusRequest(string Status, string? Reason = null);

public sealed record UpdateTenantPlanRequest(string SubscriptionPlan, string? Reason = null, string EffectiveDate = "Immediately");

public sealed record UpdateTenantCapacityAddonRequest(int AdditionalSlotCapacity, string? Reason = null);

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
    decimal? PricePerSlot,
    int? PurchasedSlotCapacityPerLocation,
    bool CapacityPricingEnabled,
    int AdditionalSlotCapacity,
    int? EffectiveMaximumSlotsPerLocation,
    int ActiveLocationCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TenantAuditLogResponse(
    Guid Id,
    string Action,
    string Administrator,
    string? Reason,
    string? OldValuesJson,
    string? NewValuesJson,
    DateTimeOffset CreatedAt);

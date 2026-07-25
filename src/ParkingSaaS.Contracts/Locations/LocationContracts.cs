namespace ParkingSaaS.Contracts.Locations;

public sealed record CreateLocationRequest(
    string Name,
    string Slug,
    string? Address,
    string Timezone,
    bool AllowCashPayment,
    int SlotCapacity);

public sealed record UpdateLocationRequest(
    string Name,
    string? Address,
    string Timezone,
    bool AllowCashPayment,
    int SlotCapacity = 20,
    Guid? RatePlanId = null,
    bool ClearRatePlan = false);

public sealed record LocationResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string Slug,
    string? Address,
    string Timezone,
    string Status,
    bool AllowCashPayment,
    int SlotCapacity,
    Guid? ActiveRatePlanId,
    bool IsAddOn,
    decimal? MonthlyPrice,
    string? PublicQrCodeUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record LocationQuotaResponse(
    string SubscriptionPlan,
    int ActiveLocations,
    int? MaximumLocations,
    int? MaximumSlotsPerLocation,
    bool CanCreateLocation);

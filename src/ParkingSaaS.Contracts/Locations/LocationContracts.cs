namespace ParkingSaaS.Contracts.Locations;

public sealed record CreateLocationRequest(
    string Name,
    string Slug,
    string? Address,
    string Timezone,
    int ExitGraceMinutes,
    bool AllowCashPayment);

public sealed record UpdateLocationRequest(
    string Name,
    string? Address,
    string Timezone,
    int ExitGraceMinutes,
    bool AllowCashPayment,
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
    int ExitGraceMinutes,
    bool AllowCashPayment,
    Guid? ActiveRatePlanId,
    string? PublicQrCodeUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

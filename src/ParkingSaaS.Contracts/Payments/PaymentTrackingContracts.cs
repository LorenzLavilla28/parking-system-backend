using ParkingSaaS.Contracts.Common;

namespace ParkingSaaS.Contracts.Payments;

public sealed class PaymentQueryRequest
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public string? Provider { get; init; }
    public string? PaymentMethod { get; init; }
    public Guid? LocationId { get; init; }
    public Guid? SessionId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    public int NormalizedPage => Page < 1 ? 1 : Page;
    public int NormalizedPageSize => PageSize is < 1 or > 200 ? 25 : PageSize;
}

public sealed record PaymentSummaryResponse(
    Guid Id,
    Guid ParkingSessionId,
    Guid ParkingLocationId,
    string LocationName,
    string PlateNumberRaw,
    string Status,
    string Provider,
    string? PaymentMethod,
    decimal Amount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    string? ReceiptNumber,
    string? ProviderCheckoutSessionId,
    string? ProviderPaymentId,
    string? CustomerEmail,
    Guid? RecordedByGuardId,
    string SessionStatus,
    DateTimeOffset EntryTime,
    DateTimeOffset? ExitTime,
    decimal? FinalFee,
    decimal TotalPaid,
    DateTimeOffset? PaidExitDeadline);

public sealed class PaymentOverrideQueryRequest
{
    public Guid? LocationId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int PageSize { get; init; } = 10;

    public int NormalizedPageSize => PageSize is < 1 or > 100 ? 10 : PageSize;
}

public sealed record PaymentOverrideResponse(
    Guid Id,
    Guid ParkingSessionId,
    Guid ParkingLocationId,
    string LocationName,
    string PlateNumberRaw,
    string Action,
    string Label,
    string Reason,
    string PerformedBy,
    DateTimeOffset CreatedAt,
    decimal? FeeOverride,
    decimal? FinalFee,
    decimal TotalPaid);

public sealed record PaymentAuditResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    string Action,
    string EntityType,
    string EntityId,
    Guid? UserId,
    string? OldValuesJson,
    string? NewValuesJson,
    string? Reason,
    string? IpAddress,
    string? DeviceInformation);

public sealed record PaymentWebhookResponse(
    Guid Id,
    string Provider,
    string ProviderEventId,
    string EventType,
    string PayloadHash,
    Guid? PaymentId,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    string ProcessingStatus,
    string? FailureReason);

public sealed record PaymentTimelineItem(
    DateTimeOffset At,
    string Type,
    string Label,
    string? Detail,
    string? Status);

public sealed record PaymentSessionContext(
    Guid Id,
    string PlateNumberRaw,
    string VehicleType,
    string LocationName,
    DateTimeOffset EntryTime,
    DateTimeOffset? ExitTime,
    string Status,
    decimal? FinalFee,
    decimal TotalPaid,
    DateTimeOffset? PaidExitDeadline,
    decimal? CurrentFee = null,
    decimal? CurrentOutstanding = null);

public sealed record PaymentQuoteContext(
    Guid Id,
    decimal BaseAmount,
    decimal DiscountAmount,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Status,
    string PricingBreakdownJson);

public sealed record PaymentDetailResponse(
    PaymentSummaryResponse Payment,
    PaymentSessionContext Session,
    PaymentQuoteContext? Quote,
    IReadOnlyList<PaymentTimelineItem> Timeline,
    IReadOnlyList<PaymentAuditResponse> Audit,
    IReadOnlyList<PaymentWebhookResponse> Webhooks);

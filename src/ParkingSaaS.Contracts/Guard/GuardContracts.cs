namespace ParkingSaaS.Contracts.Guard;

public sealed record RecordEntryRequest(
    Guid ParkingLocationId,
    string PlateNumber,
    string VehicleType,
    string? Notes,
    string? EntryPhotoUrl);

/// <summary>
/// Returned once at entry. Carries the raw public token, ticket code, payment
/// URL and a ready-to-render QR image so the guard can print or display it.
/// These raw values are not persisted in plaintext.
/// </summary>
public sealed record EntryTicketResponse(
    Guid SessionId,
    string PlateNumber,
    string VehicleType,
    DateTimeOffset EntryTime,
    string TicketCode,
    string PaymentUrl,
    string QrCodeDataUri,
    string LocationName);

public sealed record SessionSummaryResponse(
    Guid Id,
    Guid ParkingLocationId,
    string LocationName,
    string PlateNumberRaw,
    string VehicleType,
    string? Notes,
    DateTimeOffset EntryTime,
    string Status,
    bool PricingAvailable,
    string Currency,
    decimal CurrentFee,
    decimal Outstanding,
    decimal? FinalFee,
    decimal TotalPaid,
    DateTimeOffset? PaidExitDeadline);

public sealed record SessionSearchResponse(
    IReadOnlyList<SessionSummaryResponse> Items,
    int Page,
    int PageSize,
    long TotalCount,
    long AttentionCount,
    long UnpaidCount,
    long LongRunningCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Reprint/redisplay payload for an existing session's QR.</summary>
public sealed record SessionQrResponse(
    Guid SessionId,
    string TicketCode,
    string PaymentUrl,
    string QrCodeDataUri);

public sealed record PlateScanBoundingBox(int Left, int Top, int Width, int Height);

public sealed record PlateScanResponse(
    bool Detected,
    string? PlateNumber,
    double? Confidence,
    double? OcrConfidence,
    PlateScanBoundingBox? BoundingBox,
    int? ImageWidth,
    int? ImageHeight);

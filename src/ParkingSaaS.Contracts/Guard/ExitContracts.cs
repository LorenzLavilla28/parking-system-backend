namespace ParkingSaaS.Contracts.Guard;

/// <summary>
/// The large, clear status a guard sees at exit. <see cref="Decision"/> drives the
/// banner: Paid / Free / NotPaid / AdditionalPaymentRequired / Closed.
/// </summary>
public sealed record ExitStatusResponse(
    Guid SessionId,
    string PlateNumberRaw,
    string VehicleType,
    string? Notes,
    string Status,
    string Decision,
    bool PricingAvailable,
    string Currency,
    decimal CurrentFee,
    decimal TotalPaid,
    decimal Outstanding,
    DateTimeOffset EntryTime,
    DateTimeOffset? PaidExitDeadline,
    bool CanApproveExit);

public sealed record ApproveExitRequest(
    Guid SessionId,
    string? ExitPhotoUrl,
    string? DeviceInformation,
    string? OverrideReason,
    decimal? CashPaymentAmount = null);

public sealed record ExitApprovedResponse(
    Guid SessionId,
    string Status,
    decimal FinalFee,
    decimal TotalPaid,
    DateTimeOffset ExitTime);

public sealed record RecordCashPaymentRequest(
    Guid SessionId,
    decimal AmountReceived,
    string? DeviceInformation);

public sealed record CashReceiptResponse(
    Guid PaymentId,
    string ReceiptNumber,
    decimal AmountDue,
    decimal AmountReceived,
    decimal Change,
    string Currency,
    DateTimeOffset PaidAt,
    string SessionStatus,
    DateTimeOffset? PaidExitDeadline);

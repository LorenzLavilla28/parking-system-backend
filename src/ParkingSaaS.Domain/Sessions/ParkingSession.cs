using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Sessions;

/// <summary>
/// A vehicle's stay at a parking location, created by a guard at entry. Public
/// access tokens and the ticket code are stored as deterministic hashes (for
/// lookup) plus a protected/encrypted form (so the QR can be reprinted). The
/// backend is the single source of truth for payment and exit state.
/// </summary>
public class ParkingSession : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ParkingLocationId { get; private set; }

    public string PlateNumberRaw { get; private set; } = string.Empty;
    public string PlateNumberNormalized { get; private set; } = string.Empty;
    public VehicleType VehicleType { get; private set; }
    public string? VehicleColor { get; private set; }

    public DateTimeOffset EntryTime { get; private set; }
    public DateTimeOffset? ExitTime { get; private set; }
    public ParkingSessionStatus Status { get; private set; } = ParkingSessionStatus.ActiveUnpaid;

    /// <summary>The rate plan version pinned at entry. Populated by the pricing engine (Phase 3).</summary>
    public Guid? RatePlanVersionId { get; private set; }

    public string PublicTokenHash { get; private set; } = string.Empty;
    public string PublicTokenProtected { get; private set; } = string.Empty;
    public string TicketCodeHash { get; private set; } = string.Empty;
    public string TicketCodeProtected { get; private set; } = string.Empty;

    public Guid CreatedByGuardId { get; private set; }
    public Guid? ExitedByGuardId { get; private set; }
    public string? EntryPhotoUrl { get; private set; }
    public string? ExitPhotoUrl { get; private set; }

    public DateTimeOffset? PaidExitDeadline { get; private set; }
    public decimal? FinalFee { get; private set; }
    public decimal TotalPaid { get; private set; }

    /// <summary>
    /// Supervisor-set authoritative fee that overrides the rate-plan calculation.
    /// Used for complimentary parking (0) and fee waivers. Null = use calculation.
    /// </summary>
    public decimal? FeeOverride { get; private set; }

    /// <summary>Optimistic concurrency token (mapped to PostgreSQL's xmin system column).</summary>
    public uint ConcurrencyToken { get; private set; }

    private ParkingSession() { }

    public static ParkingSession RecordEntry(
        Guid tenantId,
        Guid parkingLocationId,
        Guid createdByGuardId,
        string plateRaw,
        string plateNormalized,
        VehicleType vehicleType,
        string? vehicleColor,
        DateTimeOffset entryTime,
        string? entryPhotoUrl)
    {
        if (tenantId == Guid.Empty) throw new DomainException("session.tenant_required", "Tenant is required.");
        if (parkingLocationId == Guid.Empty) throw new DomainException("session.location_required", "Location is required.");
        if (string.IsNullOrWhiteSpace(plateNormalized))
            throw new DomainException("session.plate_required", "A plate number is required.");

        return new ParkingSession
        {
            TenantId = tenantId,
            ParkingLocationId = parkingLocationId,
            CreatedByGuardId = createdByGuardId,
            PlateNumberRaw = plateRaw.Trim(),
            PlateNumberNormalized = plateNormalized,
            VehicleType = vehicleType,
            VehicleColor = string.IsNullOrWhiteSpace(vehicleColor) ? null : vehicleColor.Trim(),
            EntryTime = entryTime,
            Status = ParkingSessionStatus.ActiveUnpaid,
            EntryPhotoUrl = entryPhotoUrl,
            TotalPaid = 0m
        };
    }

    /// <summary>Stores the lookup hashes and reprint-recoverable protected token forms.</summary>
    public void AssignTokens(string publicTokenHash, string publicTokenProtected, string ticketCodeHash, string ticketCodeProtected)
    {
        PublicTokenHash = publicTokenHash;
        PublicTokenProtected = publicTokenProtected;
        TicketCodeHash = ticketCodeHash;
        TicketCodeProtected = ticketCodeProtected;
    }

    public void SetRatePlanVersion(Guid ratePlanVersionId) => RatePlanVersionId = ratePlanVersionId;

    /// <summary>Moves an unpaid session into the payment-pending state when a checkout is created.</summary>
    public void MarkPaymentPending()
    {
        if (Status == ParkingSessionStatus.ActiveUnpaid)
            Status = ParkingSessionStatus.PaymentPending;
    }

    /// <summary>
    /// Returns a payment-pending session to unpaid when the online checkout was
    /// abandoned/expired/cancelled, so the customer can start over. No-op unless the
    /// session is still <see cref="ParkingSessionStatus.PaymentPending"/>, so a session
    /// that has since been paid, exited, or closed is never disturbed.
    /// </summary>
    public void RevertToUnpaid()
    {
        if (Status == ParkingSessionStatus.PaymentPending)
            Status = ParkingSessionStatus.ActiveUnpaid;
    }

    /// <summary>
    /// Records a successful payment: accumulates the amount paid, opens the paid
    /// exit window, and stores the deadline. Idempotent re-application of the same
    /// amount is prevented by the caller (webhook idempotency).
    /// </summary>
    public void RegisterPayment(decimal amount, DateTimeOffset paidExitDeadline)
    {
        EnsureOpen();
        TotalPaid += amount;
        PaidExitDeadline = paidExitDeadline;
        Status = ParkingSessionStatus.PaidExitWindow;
    }

    /// <summary>Repairs the deadline for an existing paid session without changing payment history.</summary>
    public void CorrectPaidExitDeadline(DateTimeOffset paidExitDeadline)
    {
        if (Status is not (ParkingSessionStatus.PaidExitWindow or ParkingSessionStatus.OverstayDue))
            throw new DomainException("session.not_paid", "Only a paid session can have its exit deadline corrected.");

        PaidExitDeadline = paidExitDeadline;
    }

    /// <summary>Transitions a paid session to overstay when its exit deadline has passed and a balance is due.</summary>
    public void MarkOverstay()
    {
        if (Status == ParkingSessionStatus.PaidExitWindow)
            Status = ParkingSessionStatus.OverstayDue;
    }

    /// <summary>
    /// Applies the time-based lifecycle transition without changing any fee data.
    /// An expired exit window alone is not enough to make a session overdue: the
    /// recalculated fee must exceed the amount already paid. Callers may safely
    /// invoke this repeatedly.
    /// </summary>
    public void RefreshTimeBasedStatus(DateTimeOffset now, decimal calculatedFee)
    {
        if (Status is not (ParkingSessionStatus.PaidExitWindow or ParkingSessionStatus.OverstayDue) ||
            PaidExitDeadline is not { } deadline)
            return;

        if (deadline > now)
        {
            if (Status == ParkingSessionStatus.OverstayDue)
                Status = ParkingSessionStatus.PaidExitWindow;
            return;
        }

        Status = Outstanding(calculatedFee) > 0m
            ? ParkingSessionStatus.OverstayDue
            : ParkingSessionStatus.PaidExitWindow;
    }

    /// <summary>Returns the state that should be shown at the supplied instant.</summary>
    public ParkingSessionStatus EffectiveStatus(DateTimeOffset now, decimal calculatedFee)
    {
        if (Status is (ParkingSessionStatus.PaidExitWindow or ParkingSessionStatus.OverstayDue) &&
            PaidExitDeadline is { } deadline && deadline <= now)
            return Outstanding(calculatedFee) > 0m
                ? ParkingSessionStatus.OverstayDue
                : ParkingSessionStatus.PaidExitWindow;

        return Status;
    }

    /// <summary>
    /// Records guard-approved exit. The caller must have revalidated that no
    /// outstanding balance remains (or that an authorized override applies).
    /// </summary>
    public void ApproveExit(Guid guardId, DateTimeOffset exitTime, decimal finalFee, string? exitPhotoUrl)
    {
        EnsureOpen();
        ExitedByGuardId = guardId;
        ExitTime = exitTime;
        FinalFee = finalFee;
        if (!string.IsNullOrWhiteSpace(exitPhotoUrl))
            ExitPhotoUrl = exitPhotoUrl;
        Status = ParkingSessionStatus.Exited;
    }

    public void Void()
    {
        if (Status == ParkingSessionStatus.Exited)
            throw new DomainException("session.exited", "An exited session cannot be voided.");
        Status = ParkingSessionStatus.Void;
    }

    public void Cancel()
    {
        if (Status == ParkingSessionStatus.Exited)
            throw new DomainException("session.exited", "An exited session cannot be cancelled.");
        Status = ParkingSessionStatus.Cancelled;
    }

    /// <summary>Marks the stay complimentary: the effective fee becomes zero.</summary>
    public void MarkComplimentary()
    {
        EnsureOpen();
        FeeOverride = 0m;
    }

    /// <summary>Waives any outstanding balance: the effective fee is pinned to what was already paid.</summary>
    public void WaiveOutstanding()
    {
        EnsureOpen();
        FeeOverride = TotalPaid;
    }

    public void CorrectEntryTime(DateTimeOffset newEntryTime, DateTimeOffset now)
    {
        EnsureOpen();
        if (newEntryTime > now.AddMinutes(1))
            throw new DomainException("session.entry_future", "Entry time cannot be in the future.");
        EntryTime = newEntryTime;
    }

    public void CorrectPlate(string plateRaw, string plateNormalized)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(plateNormalized))
            throw new DomainException("session.plate_required", "A plate number is required.");
        PlateNumberRaw = plateRaw.Trim();
        PlateNumberNormalized = plateNormalized;
    }

    /// <summary>The authoritative fee for this session: an override if set, otherwise the calculated amount.</summary>
    public decimal EffectiveFee(decimal calculatedFee) => FeeOverride ?? calculatedFee;

    /// <summary>Outstanding balance given the calculated fee, never negative.</summary>
    public decimal Outstanding(decimal calculatedFee) => Math.Max(0m, EffectiveFee(calculatedFee) - TotalPaid);

    private void EnsureOpen()
    {
        if (Status is ParkingSessionStatus.Exited or ParkingSessionStatus.Void or ParkingSessionStatus.Cancelled)
            throw new DomainException("session.closed", "This session is closed.");
    }

    public bool IsActive => Status.IsActive();
}

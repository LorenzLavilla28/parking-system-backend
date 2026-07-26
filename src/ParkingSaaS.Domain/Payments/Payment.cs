using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Payments;

/// <summary>
/// A payment attempt against a fee quote. Online payments are created Pending,
/// move to Paid only when the verified provider webhook arrives, and are
/// idempotent on re-delivery. Cash payments (Phase 5) reuse the same entity with
/// <see cref="PaymentProvider.Cash"/> and are recorded already Paid.
/// Successful payment history is never overwritten.
/// </summary>
public class Payment : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ParkingSessionId { get; private set; }
    public Guid? FeeQuoteId { get; private set; }
    public PaymentProvider Provider { get; private set; }

    public string? ProviderCheckoutSessionId { get; private set; }
    public string? ProviderCheckoutUrl { get; private set; }
    public string? ProviderPaymentId { get; private set; }
    public string? ProviderAccountId { get; private set; }

    public string Currency { get; private set; } = "PHP";
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    /// <summary>Customer email captured at checkout, used to send an online receipt (optional).</summary>
    public string? CustomerEmail { get; private set; }

    /// <summary>Provider method detail (e.g. "card", "gcash") or "cash".</summary>
    public string? PaymentMethod { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }

    /// <summary>SHA-256 hash of the opaque public reference used by the status page (indexed lookup).</summary>
    public string PublicReferenceHash { get; private set; } = string.Empty;

    /// <summary>Encrypted form of the public reference so it can be recovered for an idempotent retry.</summary>
    public string PublicReferenceProtected { get; private set; } = string.Empty;

    /// <summary>Idempotency key sent to the provider and used to de-duplicate checkout creation.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Human-readable receipt number for cash payments (null for online).</summary>
    public string? ReceiptNumber { get; private set; }

    /// <summary>Guard who recorded a cash payment (null for online).</summary>
    public Guid? RecordedByGuardId { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint ConcurrencyToken { get; private set; }

    private Payment() { }

    public static Payment CreateOnlinePending(
        Guid tenantId, Guid parkingSessionId, Guid feeQuoteId, string currency, decimal amount,
        string publicReferenceHash, string publicReferenceProtected, string idempotencyKey,
        string? customerEmail = null)
    {
        if (amount <= 0m) throw new DomainException("payment.amount_invalid", "Amount must be positive.");
        return new Payment
        {
            TenantId = tenantId,
            ParkingSessionId = parkingSessionId,
            FeeQuoteId = feeQuoteId,
            Provider = PaymentProvider.PayMongo,
            Currency = currency,
            Amount = amount,
            Status = PaymentStatus.Pending,
            CustomerEmail = string.IsNullOrWhiteSpace(customerEmail) ? null : customerEmail.Trim(),
            PublicReferenceHash = publicReferenceHash,
            PublicReferenceProtected = publicReferenceProtected,
            IdempotencyKey = idempotencyKey
        };
    }

    public static Payment CreateCashPaid(
        Guid tenantId, Guid parkingSessionId, Guid? feeQuoteId, string currency, decimal amount,
        string publicReferenceHash, string publicReferenceProtected, DateTimeOffset paidAt,
        string receiptNumber, Guid recordedByGuardId)
    {
        if (amount <= 0m) throw new DomainException("payment.amount_invalid", "Amount must be positive.");
        return new Payment
        {
            TenantId = tenantId,
            ParkingSessionId = parkingSessionId,
            FeeQuoteId = feeQuoteId,
            Provider = PaymentProvider.Cash,
            Currency = currency,
            Amount = amount,
            Status = PaymentStatus.Paid,
            PaymentMethod = "cash",
            PaidAt = paidAt,
            PublicReferenceHash = publicReferenceHash,
            PublicReferenceProtected = publicReferenceProtected,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            ReceiptNumber = receiptNumber,
            RecordedByGuardId = recordedByGuardId
        };
    }

    public void SetCheckoutSession(string providerCheckoutSessionId, string? checkoutUrl)
    {
        ProviderCheckoutSessionId = providerCheckoutSessionId;
        ProviderCheckoutUrl = checkoutUrl;
    }

    public void SetProviderAccountId(string? providerAccountId)
        => ProviderAccountId = string.IsNullOrWhiteSpace(providerAccountId) ? null : providerAccountId.Trim();

    public void MarkProcessing()
    {
        if (Status == PaymentStatus.Pending)
            Status = PaymentStatus.Processing;
    }

    /// <summary>
    /// Transitions to Paid. Idempotent: re-delivering the same paid webhook on an
    /// already-paid payment is a no-op, so the original payment record is preserved.
    /// </summary>
    public void MarkPaid(string providerPaymentId, string? paymentMethod, DateTimeOffset paidAt)
    {
        if (Status == PaymentStatus.Paid)
            return;
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
            throw new DomainException("payment.invalid_transition", $"Cannot pay a {Status} payment.");

        Status = PaymentStatus.Paid;
        ProviderPaymentId = providerPaymentId;
        PaymentMethod = paymentMethod;
        PaidAt = paidAt;
    }

    public void MarkFailed()
    {
        if (Status is PaymentStatus.Pending or PaymentStatus.Processing)
            Status = PaymentStatus.Failed;
    }

    public void MarkExpired()
    {
        if (Status is PaymentStatus.Pending or PaymentStatus.Processing)
            Status = PaymentStatus.Expired;
    }

    public void Cancel()
    {
        if (Status is PaymentStatus.Pending or PaymentStatus.Processing)
            Status = PaymentStatus.Cancelled;
    }

    public bool IsSettled => Status is PaymentStatus.Paid or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded;
    public bool IsOpen => Status is PaymentStatus.Pending or PaymentStatus.Processing;
}

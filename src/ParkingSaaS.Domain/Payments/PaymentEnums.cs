namespace ParkingSaaS.Domain.Payments;

public enum PaymentProvider
{
    PayMongo = 1,
    Cash = 2
}

/// <summary>
/// Payment lifecycle. The webhook is the source of truth for the transition to
/// <see cref="Paid"/>; redirect pages never drive state.
/// </summary>
public enum PaymentStatus
{
    Pending = 1,
    Processing = 2,
    Paid = 3,
    Failed = 4,
    Expired = 5,
    Cancelled = 6,
    Refunded = 7,
    PartiallyRefunded = 8
}

/// <summary>Processing state of a stored provider webhook event.</summary>
public enum WebhookProcessingStatus
{
    Received = 1,
    Processed = 2,
    Failed = 3,
    Ignored = 4
}

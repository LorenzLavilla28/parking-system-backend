namespace ParkingSaaS.Domain.Emails;

/// <summary>What a queued email is for. Drives templating and reporting.</summary>
public enum EmailKind
{
    TenantOnboarding = 1,
    PaymentReceipt = 2,
    UserWelcome = 3,
    PasswordReset = 4,
    OverstayNotice = 5,
    OperationsSummary = 6
}

/// <summary>
/// Lifecycle of a queued (outbox) email. A message stays <see cref="Pending"/>
/// across retries; it only leaves that state on success (<see cref="Sent"/>) or
/// after exhausting its attempts (<see cref="Failed"/>, i.e. dead-lettered).
/// </summary>
public enum EmailStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3
}

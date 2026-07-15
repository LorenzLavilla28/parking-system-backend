using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Emails;

/// <summary>
/// A queued outbound email (transactional-outbox row). Created inside the same
/// transaction as the business change that triggers it, so an email is never lost
/// nor sent for an operation that rolled back. A background dispatcher later sends
/// it and records the result, retrying with backoff and dead-lettering after a
/// bounded number of attempts. Provider-global (not tenant-owned); TenantId is
/// stored for reporting only, so the dispatcher can run without a tenant scope.
/// </summary>
public class EmailMessage : AggregateRoot
{
    public Guid? TenantId { get; private set; }
    public EmailKind Kind { get; private set; }

    public string ToEmail { get; private set; } = string.Empty;
    public string? ToName { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string HtmlBody { get; private set; } = string.Empty;
    public string TextBody { get; private set; } = string.Empty;

    public EmailStatus Status { get; private set; } = EmailStatus.Pending;
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; } = 5;

    /// <summary>Earliest time the dispatcher should (re)try this message.</summary>
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint ConcurrencyToken { get; private set; }

    private EmailMessage() { }

    public static EmailMessage Create(
        EmailKind kind, string toEmail, string? toName, string subject, string htmlBody, string textBody,
        DateTimeOffset now, Guid? tenantId = null, int maxAttempts = 5)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            throw new DomainException("email.recipient_required", "A recipient email address is required.");
        if (string.IsNullOrWhiteSpace(subject))
            throw new DomainException("email.subject_required", "An email subject is required.");

        return new EmailMessage
        {
            Kind = kind,
            TenantId = tenantId,
            ToEmail = toEmail.Trim(),
            ToName = string.IsNullOrWhiteSpace(toName) ? null : toName.Trim(),
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = textBody,
            Status = EmailStatus.Pending,
            MaxAttempts = maxAttempts < 1 ? 1 : maxAttempts,
            NextAttemptAt = now,
            CreatedAt = now
        };
    }

    /// <summary>Whether the dispatcher should attempt this message at <paramref name="now"/>.</summary>
    public bool IsDue(DateTimeOffset now) => Status == EmailStatus.Pending && NextAttemptAt <= now;

    public void MarkSent(DateTimeOffset now)
    {
        AttemptCount++;
        Status = EmailStatus.Sent;
        SentAt = now;
        LastError = null;
    }

    /// <summary>
    /// Records a failed send. Reschedules for a later retry (staying Pending) until
    /// <see cref="MaxAttempts"/> is reached, after which the message is dead-lettered
    /// (<see cref="EmailStatus.Failed"/>) and no longer retried.
    /// </summary>
    public void MarkFailed(DateTimeOffset now, string error)
    {
        AttemptCount++;
        LastError = Truncate(error, 1000);

        if (AttemptCount >= MaxAttempts)
        {
            Status = EmailStatus.Failed;
            return;
        }

        Status = EmailStatus.Pending;
        NextAttemptAt = now.Add(BackoffFor(AttemptCount));
    }

    // Exponential-ish backoff capped at 1 hour: 1, 5, 15, 30, 60 minutes.
    private static TimeSpan BackoffFor(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        4 => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromMinutes(60)
    };

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

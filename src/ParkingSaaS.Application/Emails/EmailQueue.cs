using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;

namespace ParkingSaaS.Application.Emails;

/// <summary>
/// Transactional-outbox implementation of <see cref="IEmailQueue"/>. Renders the
/// message via <see cref="EmailTemplates"/> and stages it on the DbContext; it does
/// not call SaveChanges, so the email commits atomically with the caller's change.
/// </summary>
public sealed class EmailQueue : IEmailQueue
{
    private readonly IApplicationDbContext _db;
    private readonly EmailOptions _options;

    public EmailQueue(IApplicationDbContext db, IOptions<EmailOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public void QueueTenantOnboarding(Guid tenantId, string toEmail, string adminName, string tenantName, string tenantSlug, string temporaryPassword, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) return;
        _db.Emails.Add(EmailTemplates.TenantOnboarding(
            tenantId, toEmail, adminName, tenantName, tenantSlug, temporaryPassword, _options.AppBaseUrl, now, _options.MaxAttempts));
    }

    public void QueuePaymentReceipt(Guid tenantId, string toEmail, PaymentReceiptEmailData data, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) return;
        _db.Emails.Add(EmailTemplates.PaymentReceipt(tenantId, toEmail, data, now, _options.MaxAttempts));
    }

    public void QueueOverstayNotice(Guid tenantId, string toEmail, OverstayNoticeEmailData data, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) return;
        _db.Emails.Add(EmailTemplates.OverstayNotice(tenantId, toEmail, data, now, _options.MaxAttempts));
    }

    public void QueueOperationsSummary(Guid tenantId, string toEmail, string? toName, OperationsSummaryEmailData data, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) return;
        _db.Emails.Add(EmailTemplates.OperationsSummary(tenantId, toEmail, toName, data, now, _options.MaxAttempts));
    }

    public void QueueUserWelcome(Guid tenantId, string toEmail, string userName, string tenantName, IReadOnlyCollection<string> roles, string temporaryPassword, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) return;
        _db.Emails.Add(EmailTemplates.UserWelcome(
            tenantId, toEmail, userName, tenantName, roles, temporaryPassword, _options.AppBaseUrl, now, _options.MaxAttempts));
    }

    public void QueuePasswordReset(Guid tenantId, string toEmail, string userName, string resetUrl, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) return;
        _db.Emails.Add(EmailTemplates.PasswordReset(
            tenantId, toEmail, userName, resetUrl, now, _options.MaxAttempts));
    }
}

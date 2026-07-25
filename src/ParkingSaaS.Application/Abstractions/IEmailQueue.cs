namespace ParkingSaaS.Application.Abstractions;

using ParkingSaaS.Contracts.Reports;

/// <summary>Data needed to render an online-payment receipt email.</summary>
public sealed record PaymentReceiptEmailData(
    string PlateNumber,
    string LocationName,
    decimal Amount,
    string Currency,
    DateTimeOffset PaidAt,
    string? PaymentMethod,
    string Reference,
    DateTimeOffset? PaidExitDeadline);

public sealed record OverstayNoticeEmailData(
    string PlateNumber,
    string LocationName,
    DateTimeOffset PaidExitDeadline,
    string PaymentUrl,
    string QrCodeDataUri);

public sealed record OperationsSummaryEmailData(
    OperationsSummaryResponse Summary,
    string DashboardUrl);

/// <summary>
/// Enqueues outbound emails as part of the caller's unit of work. Each method only
/// stages the message on the DbContext (transactional outbox); the caller's
/// <c>SaveChangesAsync</c> commits it atomically with the triggering change, and a
/// background dispatcher sends it. Enqueuing never sends inline, so a slow or down
/// mail server can't fail or delay the request.
/// </summary>
public interface IEmailQueue
{
    void QueueTenantOnboarding(Guid tenantId, string toEmail, string adminName, string tenantName, string tenantSlug, string temporaryPassword, DateTimeOffset now);

    void QueuePaymentReceipt(Guid tenantId, string toEmail, PaymentReceiptEmailData data, DateTimeOffset now);

    void QueueOverstayNotice(Guid tenantId, string toEmail, OverstayNoticeEmailData data, DateTimeOffset now);

    void QueueOperationsSummary(Guid tenantId, string toEmail, string? toName,
        OperationsSummaryEmailData data, DateTimeOffset now);

    void QueueUserWelcome(Guid tenantId, string toEmail, string userName, string tenantName, IReadOnlyCollection<string> roles, string temporaryPassword, DateTimeOffset now);

    void QueuePasswordReset(Guid tenantId, string toEmail, string userName, string resetUrl, DateTimeOffset now);
}

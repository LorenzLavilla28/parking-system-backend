using System.Globalization;
using System.Net;
using System.Text;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Domain.Emails;

namespace ParkingSaaS.Application.Emails;

/// <summary>
/// Builds the concrete <see cref="EmailMessage"/> for each notification. Kept as
/// pure functions (no I/O) so templates are trivially unit-testable and the queue
/// stays a thin persistence seam. All interpolated values are HTML-encoded.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage TenantOnboarding(
        Guid tenantId, string toEmail, string adminName, string tenantName, string tenantSlug,
        string temporaryPassword, string appBaseUrl, DateTimeOffset now, int maxAttempts)
    {
        var name = E(adminName);
        var org = E(tenantName);
        var loginUrl = $"{appBaseUrl.TrimEnd('/')}/login";
        var subject = $"Welcome to ParkingSaaS — {tenantName} is ready";

        var html = Wrap($"Welcome, {name}", $$"""
            <p>Your ParkingSaaS workspace for <strong>{{org}}</strong> has been created and is ready to use.</p>
            <p>You're set up as the tenant administrator. Sign in to add locations, staff, and rate plans.</p>
            <p><strong>Temporary password:</strong> <code>{{E(temporaryPassword)}}</code></p>
            <p style="color:#b45309;font-size:13px">For your security, you must change this password the first time you sign in.</p>
            <p style="margin:24px 0"><a href="{{E(loginUrl)}}" style="background:#0f172a;color:#fff;padding:12px 20px;border-radius:8px;text-decoration:none;display:inline-block">Sign in to ParkingSaaS</a></p>
            <p style="color:#64748b;font-size:13px">Workspace: {{E(tenantSlug)}}</p>
            """);

        var text = $"Welcome, {adminName}\n\nYour ParkingSaaS workspace for {tenantName} is ready. " +
                   $"You're the tenant administrator. Sign in: {loginUrl}\nTemporary password: {temporaryPassword}\n" +
                   $"You must change this password the first time you sign in.\nWorkspace: {tenantSlug}";

        return EmailMessage.Create(EmailKind.TenantOnboarding, toEmail, adminName, subject, html, text, now, tenantId, maxAttempts);
    }

    public static EmailMessage PaymentReceipt(
        Guid tenantId, string toEmail, PaymentReceiptEmailData d, DateTimeOffset now, int maxAttempts)
    {
        var amount = FormatMoney(d.Amount, d.Currency);
        var paidAt = d.PaidAt.ToString("dd MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);
        var subject = $"Parking receipt — {amount} ({d.PlateNumber})";

        var deadlineRow = d.PaidExitDeadline is { } dl
            ? Row("Exit before", E(dl.ToString("dd MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture)))
            : string.Empty;

        var html = Wrap("Payment received", $$"""
            <p>Thanks — your parking payment has been received.</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              {{Row("Amount", $"<strong>{E(amount)}</strong>")}}
              {{Row("Plate", E(d.PlateNumber))}}
              {{Row("Location", E(d.LocationName))}}
              {{Row("Paid at", E(paidAt))}}
              {{Row("Method", E(d.PaymentMethod ?? "online"))}}
              {{Row("Reference", E(d.Reference))}}
              {{deadlineRow}}
            </table>
            <p style="color:#64748b;font-size:13px">Keep this receipt for your records.</p>
            """);

        var text = new StringBuilder()
            .AppendLine("Payment received")
            .AppendLine($"Amount: {amount}")
            .AppendLine($"Plate: {d.PlateNumber}")
            .AppendLine($"Location: {d.LocationName}")
            .AppendLine($"Paid at: {paidAt}")
            .AppendLine($"Method: {d.PaymentMethod ?? "online"}")
            .AppendLine($"Reference: {d.Reference}")
            .ToString();

        return EmailMessage.Create(EmailKind.PaymentReceipt, toEmail, null, subject, html, text, now, tenantId, maxAttempts);
    }

    public static EmailMessage OverstayNotice(
        Guid tenantId, string toEmail, OverstayNoticeEmailData d, DateTimeOffset now, int maxAttempts)
    {
        var deadline = d.PaidExitDeadline.ToString("dd MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);
        var subject = $"Parking exit window passed ({d.PlateNumber})";
        var html = Wrap("Exit window passed", $$"""
            <p>Your paid exit window for <strong>{{E(d.PlateNumber)}}</strong> at {{E(d.LocationName)}} has passed.</p>
            <p>The current parking fee will be recalculated when you exit. Please proceed to the exit desk or open your parking payment page to settle any additional amount.</p>
            <p style="color:#b45309;font-size:13px">Original exit deadline: {{E(deadline)}}</p>
            """);
        var text = $"Your paid exit window for {d.PlateNumber} at {d.LocationName} has passed. " +
                   $"The current parking fee will be recalculated when you exit. Original deadline: {deadline}.";
        return EmailMessage.Create(EmailKind.OverstayNotice, toEmail, null, subject, html, text, now, tenantId, maxAttempts);
    }

    public static EmailMessage OperationsSummary(
        Guid tenantId, string toEmail, string? toName, OperationsSummaryEmailData d,
        DateTimeOffset now, int maxAttempts)
    {
        var summary = d.Summary;
        var periodStart = FormatInTimeZone(summary.PeriodStart, summary.TimeZone);
        var periodEnd = FormatInTimeZone(summary.PeriodEnd, summary.TimeZone);
        var revenue = FormatMoney(summary.Revenue, summary.Currency);
        var subject = $"ParkingSaaS operations summary — {summary.TenantName}";
        var breakdown = string.Join("", summary.PaymentBreakdown.Select(item =>
            Row(E(item.Label), $"{item.Count} / {E(FormatMoney(item.Amount, summary.Currency))}")));
        var attention = summary.Attention.Count == 0
            ? "<p style=\"color:#047857\">No review items were detected in this period.</p>"
            : string.Join("", summary.Attention.Select(item =>
                $"<div style=\"border-left:4px solid {AttentionColor(item.Severity)};background:#f8fafc;padding:10px 12px;margin:8px 0\"><strong>{E(item.Title)}</strong><br/><span style=\"font-size:13px\">{E(item.Detail)}</span></div>"));

        var html = Wrap("3-hour operations summary", $$"""
            <p>Here is the latest activity summary for <strong>{{E(summary.TenantName)}}</strong>.</p>
            <p style="color:#64748b;font-size:13px">{{E(periodStart)}} — {{E(periodEnd)}} ({{E(summary.TimeZone)}})</p>
            <table style="width:100%;border-collapse:collapse;margin:16px 0">
              {{Row("Revenue", $"<strong>{E(revenue)}</strong>")}}
              {{Row("Session entries", E(summary.SessionEntries.ToString(CultureInfo.InvariantCulture)))}}
              {{Row("Session exits", E(summary.SessionExits.ToString(CultureInfo.InvariantCulture)))}}
              {{Row("Active sessions", E(summary.ActiveSessions.ToString(CultureInfo.InvariantCulture)))}}
              {{Row("Overstays", E(summary.Overstays.ToString(CultureInfo.InvariantCulture)))}}
            </table>
            <h2 style="font-size:16px;margin:24px 0 8px">Payment reconciliation</h2>
            <table style="width:100%;border-collapse:collapse;margin:8px 0">
              {{Row("Category", "<strong>Count / amount</strong>")}}
              {{breakdown}}
            </table>
            <p style="color:#64748b;font-size:13px">Pending: {{E(FormatMoney(summary.PendingAmount, summary.Currency))}} · Failed/closed: {{E(FormatMoney(summary.FailedAmount, summary.Currency))}} · Failed webhooks: {{E(summary.FailedWebhooks.ToString(CultureInfo.InvariantCulture))}}</p>
            <h2 style="font-size:16px;margin:24px 0 8px">Attention required</h2>
            {{attention}}
            <p style="margin:24px 0"><a href="{{E(d.DashboardUrl)}}" style="background:#0f172a;color:#fff;padding:12px 20px;border-radius:8px;text-decoration:none;display:inline-block">Review in ParkingSaaS</a></p>
            """);

        var text = new StringBuilder()
            .AppendLine("3-hour operations summary")
            .AppendLine($"Tenant: {summary.TenantName}")
            .AppendLine($"Period: {periodStart} — {periodEnd} ({summary.TimeZone})")
            .AppendLine($"Revenue: {revenue}")
            .AppendLine($"Session entries: {summary.SessionEntries}")
            .AppendLine($"Session exits: {summary.SessionExits}")
            .AppendLine($"Active sessions: {summary.ActiveSessions}")
            .AppendLine($"Overstays: {summary.Overstays}")
            .AppendLine($"Pending payments: {summary.PendingPayments} ({FormatMoney(summary.PendingAmount, summary.Currency)})")
            .AppendLine($"Failed/closed payments: {summary.FailedPayments} ({FormatMoney(summary.FailedAmount, summary.Currency)})")
            .AppendLine($"Failed webhooks: {summary.FailedWebhooks}")
            .AppendLine()
            .AppendLine(summary.Attention.Count == 0
                ? "Attention required: none"
                : "Attention required:\n" + string.Join("\n", summary.Attention.Select(a => $"- {a.Title}: {a.Detail}")))
            .AppendLine()
            .AppendLine($"Review in ParkingSaaS: {d.DashboardUrl}")
            .ToString();

        return EmailMessage.Create(EmailKind.OperationsSummary, toEmail, toName, subject, html, text, now, tenantId, maxAttempts);
    }

    public static EmailMessage UserWelcome(
        Guid tenantId, string toEmail, string userName, string tenantName, IReadOnlyCollection<string> roles,
        string temporaryPassword, string appBaseUrl, DateTimeOffset now, int maxAttempts)
    {
        var name = E(userName);
        var org = E(tenantName);
        var roleList = E(string.Join(", ", roles));
        var loginUrl = $"{appBaseUrl.TrimEnd('/')}/login";
        var subject = $"Your ParkingSaaS account for {tenantName}";

        var html = Wrap($"Hello, {name}", $$"""
            <p>An account has been created for you on <strong>{{org}}</strong>'s ParkingSaaS workspace.</p>
            <p>Your role(s): <strong>{{roleList}}</strong></p>
            <p><strong>Temporary password:</strong> <code>{{E(temporaryPassword)}}</code></p>
            <p style="color:#b45309;font-size:13px">You must change this password the first time you sign in.</p>
            <p style="margin:24px 0"><a href="{{E(loginUrl)}}" style="background:#0f172a;color:#fff;padding:12px 20px;border-radius:8px;text-decoration:none;display:inline-block">Sign in</a></p>
            """);

        var text = $"Hello, {userName}\n\nAn account was created for you on {tenantName}'s ParkingSaaS workspace. " +
                   $"Role(s): {string.Join(", ", roles)}.\nTemporary password: {temporaryPassword}\n" +
                   $"You must change this password the first time you sign in. Sign in: {loginUrl}";

        return EmailMessage.Create(EmailKind.UserWelcome, toEmail, userName, subject, html, text, now, tenantId, maxAttempts);
    }

    public static EmailMessage PasswordReset(
        Guid tenantId, string toEmail, string userName, string resetUrl, DateTimeOffset now, int maxAttempts)
    {
        var name = E(userName);
        var safeUrl = E(resetUrl);
        var subject = "Reset your ParkingSaaS password";

        var html = Wrap("Reset your password", $$"""
            <p>Hello, {{name}}.</p>
            <p>We received a request to reset your ParkingSaaS password. This link expires in one hour and can be used only once.</p>
            <p style="margin:24px 0"><a href="{{safeUrl}}" style="background:#0f172a;color:#fff;padding:12px 20px;border-radius:8px;text-decoration:none;display:inline-block">Reset password</a></p>
            <p style="color:#64748b;font-size:13px">If you did not request this, you can safely ignore this email.</p>
            """);

        var text = $"Hello, {userName}\n\nReset your ParkingSaaS password: {resetUrl}\n" +
                   "This link expires in one hour and can be used only once. If you did not request this, ignore this email.";

        return EmailMessage.Create(EmailKind.PasswordReset, toEmail, userName, subject, html, text, now, tenantId, maxAttempts);
    }

    private static string Row(string label, string value) =>
        $"<tr><td style=\"padding:6px 0;color:#64748b\">{E(label)}</td><td style=\"padding:6px 0;text-align:right\">{value}</td></tr>";

    private static string Wrap(string heading, string body) => $$"""
        <div style="font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;max-width:520px;margin:0 auto;color:#0f172a">
          <h1 style="font-size:20px;margin:0 0 12px">{{E(heading)}}</h1>
          {{body}}
          <hr style="border:none;border-top:1px solid #e2e8f0;margin:24px 0"/>
          <p style="color:#94a3b8;font-size:12px">ParkingSaaS — this is an automated message, please do not reply.</p>
        </div>
        """;

    private static string FormatMoney(decimal amount, string currency)
    {
        var symbol = currency.Equals("PHP", StringComparison.OrdinalIgnoreCase) ? "₱" : currency + " ";
        return symbol + amount.ToString("N2", CultureInfo.InvariantCulture);
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string FormatInTimeZone(DateTimeOffset value, string timeZone)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            return TimeZoneInfo.ConvertTime(value, zone).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
        }
        catch (TimeZoneNotFoundException)
        {
            return value.ToString("dd MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);
        }
        catch (InvalidTimeZoneException)
        {
            return value.ToString("dd MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);
        }
    }

    private static string AttentionColor(string severity) => severity switch
    {
        "danger" => "#dc2626",
        "warning" => "#d97706",
        _ => "#2563eb"
    };
}

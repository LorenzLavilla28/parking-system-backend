namespace ParkingSaaS.Application.Common.Options;

/// <summary>Base URLs used to build customer-facing QR and payment links.</summary>
public sealed class PublicUrlOptions
{
    public const string SectionName = "PublicUrls";

    /// <summary>Public site origin, e.g. https://parking.example.com (no trailing slash).</summary>
    public string BaseUrl { get; set; } = "https://parking.example.com";

    public string SessionPath(string token) => $"{BaseUrl.TrimEnd('/')}/p/{token}";
    public string LocationPath(string slug) => $"{BaseUrl.TrimEnd('/')}/location/{slug}";
    public string PaymentStatusPath(string reference) => $"{BaseUrl.TrimEnd('/')}/payment/{reference}/status";
}

/// <summary>PayMongo credentials and behaviour. Secrets come from Secrets Manager/SSM.</summary>
public sealed class PayMongoOptions
{
    public const string SectionName = "PayMongo";

    public string BaseUrl { get; set; } = "https://api.paymongo.com/v1";
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Payment methods offered on the hosted checkout, in display order. Must be values
    /// PayMongo recognises (e.g. <c>gcash</c>, <c>qrph</c>, <c>card</c>, <c>paymaya</c>).
    /// Defaults to GCash + QR Ph + card; override in configuration to change the offering.
    /// </summary>
    public string[] PaymentMethodTypes { get; set; } = ["gcash", "qrph", "card"];

    /// <summary>Reconciliation sweep interval and the age after which a pending payment is reconciled.</summary>
    public int ReconcileIntervalSeconds { get; set; } = 120;
    public int ReconcilePendingOlderThanMinutes { get; set; } = 3;
}

/// <summary>
/// Outbound email settings. When <see cref="Enabled"/> is false or no <see cref="Host"/>
/// is configured, a logging no-op sender is used (dev/CI) so queued mail is visible in
/// logs without an SMTP server. Secrets come from Secrets Manager/SSM in production.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Master switch for real delivery. False → messages are queued and logged, not sent.</summary>
    public bool Enabled { get; set; }

    /// <summary>Email transport: Gmail (SMTP) or MicrosoftGraph.</summary>
    public string Provider { get; set; } = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Microsoft Entra app-only credentials used by the Microsoft Graph transport.</summary>
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    public string FromAddress { get; set; } = "no-reply@parking.example.com";
    public string FromName { get; set; } = "ParkingSaaS";

    /// <summary>Base URL of the staff/admin SPA, used to build login links in emails.</summary>
    public string AppBaseUrl { get; set; } = "https://app.parking.example.com";

    /// <summary>How often the dispatcher drains the outbox, and how many it takes per sweep.</summary>
    public int DispatchIntervalSeconds { get; set; } = 30;
    public int DispatchBatchSize { get; set; } = 25;
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Enables the tenant-admin operations digest hosted job.</summary>
    public bool OperationsSummaryEnabled { get; set; } = true;

    /// <summary>How often the operations digest covers and sends the latest window.</summary>
    public int OperationsSummaryIntervalHours { get; set; } = 3;
}

/// <summary>Pricing/quote behaviour knobs.</summary>
public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    /// <summary>How long a created fee quote remains payable.</summary>
    public int FeeQuoteMinutes { get; set; } = 10;
}

/// <summary>Tunable thresholds for the public plate-lookup throttle.</summary>
public sealed class LookupThrottleOptions
{
    public const string SectionName = "LookupThrottle";

    public int CaptchaAfterFailures { get; set; } = 3;
    public int BlockAfterFailures { get; set; } = 8;
    public int BlockMinutes { get; set; } = 15;
    public int WindowMinutes { get; set; } = 15;
}

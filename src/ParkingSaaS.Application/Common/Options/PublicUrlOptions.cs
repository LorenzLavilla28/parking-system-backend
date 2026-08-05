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
    public bool ValidateCredentialsWithProvider { get; set; } = true;
    public string WebhookBaseUrl { get; set; } = string.Empty;
    public string SecretNamePrefix { get; set; } = "parking-saas/tenants";
    public int CredentialCacheMinutes { get; set; } = 10;

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

public sealed class AwsSecretsOptions
{
    public const string SectionName = "AwsSecrets";

    public bool Enabled { get; set; } = true;
    public string Region { get; set; } = "ap-southeast-1";
}

public sealed class TenantBrandingOptions
{
    public const string SectionName = "TenantBranding";

    /// <summary>Private S3 bucket used for tenant-owned branding assets.</summary>
    public string BucketName { get; set; } = "parking-system-tenant-assets";

    public string Region { get; set; } = "ap-southeast-1";

    /// <summary>Maximum uploaded logo size. Defaults to 2 MiB.</summary>
    public long MaxLogoBytes { get; set; } = 2 * 1024 * 1024;
}

/// <summary>
/// Outbound email settings for Microsoft Graph app-only delivery. When
/// <see cref="Enabled"/> is false, a logging no-op sender is used (dev/CI) so queued
/// mail remains visible without contacting Microsoft Graph. Secrets come from secure
/// configuration providers in production.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Master switch for real delivery. False → messages are queued and logged, not sent.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Optional AWS Secrets Manager secret containing TenantId, ClientId, ClientSecret,
    /// FromAddress, and FromName. Direct configuration values take precedence.
    /// </summary>
    public string SecretName { get; set; } = string.Empty;

    /// <summary>Microsoft Entra app-only credentials used by Microsoft Graph.</summary>
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

    /// <summary>How often the scheduler checks for tenants whose own digest interval is due.</summary>
    public int OperationsSummarySweepMinutes { get; set; } = 1;
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

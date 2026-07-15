namespace ParkingSaaS.Contracts.Reports;

public sealed record OperationsSummaryResponse(
    Guid TenantId,
    string TenantName,
    string Currency,
    string TimeZone,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateTimeOffset GeneratedAt,
    int SessionEntries,
    int SessionExits,
    int ActiveSessions,
    decimal Revenue,
    int Overstays,
    int PendingPayments,
    decimal PendingAmount,
    int FailedPayments,
    decimal FailedAmount,
    int FailedWebhooks,
    IReadOnlyList<OperationsPaymentBreakdown> PaymentBreakdown,
    IReadOnlyList<OperationsAttentionItem> Attention);

public sealed record OperationsPaymentBreakdown(
    string Label,
    int Count,
    decimal Amount);

public sealed record OperationsAttentionItem(
    string Severity,
    string Title,
    string Detail);

public sealed record OperationsSummaryEmailResponse(
    int RecipientsQueued,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd);

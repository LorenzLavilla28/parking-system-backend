namespace ParkingSaaS.Contracts.Reports;

public sealed record DashboardReportResponse(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DashboardSummaryResponse Summary,
    IReadOnlyList<RevenuePointResponse> Revenue,
    IReadOnlyList<PaymentMixResponse> PaymentMix);

public sealed record DashboardSummaryResponse(
    int ActiveSessions,
    int PaidAwaitingExit,
    int UnpaidSessions,
    int OverGraceSessions,
    decimal OverGraceAmount,
    int TodayEntries,
    int TodayExits,
    decimal TodayRevenue,
    string Currency,
    int PeriodEntries,
    int PeriodExits,
    decimal PeriodRevenue,
    double AverageDurationMinutes,
    decimal PreviousPeriodRevenue,
    int SupervisorOverrides);

public sealed record RevenuePointResponse(
    DateTimeOffset Date,
    decimal Amount,
    int PaymentCount);

public sealed record PaymentMixResponse(
    string Key,
    string Label,
    decimal Amount,
    int Count);

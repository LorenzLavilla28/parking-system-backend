using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Pricing;
using ParkingSaaS.Contracts.Reports;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Application.Reports;

/// <summary>
/// Tenant dashboard reporting. Payments are considered revenue only after they
/// reach the paid state; pending and failed attempts remain visible in the mix
/// so operators can distinguish missing revenue from missing data.
/// </summary>
public sealed class DashboardReportService : IDashboardReportService
{
    private const string Currency = "PHP";
    private static readonly string[] PaymentOverrideActions =
        ["MarkedComplimentary", "OutstandingWaived", "ExitApprovedWithOverride"];

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;
    private readonly ISessionPricingService _pricing;

    public DashboardReportService(IApplicationDbContext db, IDateTime clock, ISessionPricingService pricing)
    {
        _db = db;
        _clock = clock;
        _pricing = pricing;
    }

    public async Task<DashboardReportResponse> GetAsync(
        int days,
        Guid? parkingLocationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var todayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var normalizedDays = Math.Clamp(days, 1, 180);
        var hasCustomRange = from.HasValue && to.HasValue && from.Value < to.Value;
        var periodStart = hasCustomRange ? from!.Value : todayStart.AddDays(-(normalizedDays - 1));
        var periodEnd = hasCustomRange ? to!.Value : todayStart.AddDays(1);
        if (periodEnd - periodStart > TimeSpan.FromDays(366))
            periodStart = periodEnd.AddDays(-366);
        var periodDays = Math.Max(1, (int)Math.Ceiling((periodEnd - periodStart).TotalDays));
        var previousPeriodStart = periodStart - (periodEnd - periodStart);

        var sessionsQuery = _db.ParkingSessions
            .AsNoTracking()
            .Where(s => !parkingLocationId.HasValue || s.ParkingLocationId == parkingLocationId.Value);

        var statusCounts = await sessionsQuery
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var countByStatus = statusCounts.ToDictionary(x => x.Status, x => x.Count);
        var unpaidSessions = countByStatus.GetValueOrDefault(ParkingSessionStatus.ActiveUnpaid)
            + countByStatus.GetValueOrDefault(ParkingSessionStatus.PaymentPending);
        var paidAwaitingExit = await sessionsQuery
            .CountAsync(s => s.Status == ParkingSessionStatus.PaidExitWindow
                             && (s.PaidExitDeadline == null || s.PaidExitDeadline > now), ct);
        var persistedOverdue = countByStatus.GetValueOrDefault(ParkingSessionStatus.OverstayDue);
        var staleOverdue = await sessionsQuery
            .CountAsync(s => s.Status == ParkingSessionStatus.PaidExitWindow
                             && s.PaidExitDeadline != null && s.PaidExitDeadline <= now, ct);
        var overGraceSessions = persistedOverdue + staleOverdue;
        var overstayCandidates = await sessionsQuery
            .Where(s => s.Status == ParkingSessionStatus.OverstayDue
                        || (s.Status == ParkingSessionStatus.PaidExitWindow
                            && s.PaidExitDeadline != null && s.PaidExitDeadline <= now))
            .ToListAsync(ct);
        var overGraceAmount = 0m;
        foreach (var session in overstayCandidates)
        {
            var calculation = await _pricing.CalculateAsync(session, now, discount: null, ct);
            if (calculation is null || session.EffectiveStatus(now, calculation.TotalAmount) != ParkingSessionStatus.OverstayDue)
                continue;

            overGraceAmount += session.Outstanding(calculation.TotalAmount);
        }
        var activeSessions = unpaidSessions + paidAwaitingExit + overGraceSessions;

        var todayEntries = await sessionsQuery
            .CountAsync(s => s.EntryTime >= todayStart && s.EntryTime < periodEnd, ct);
        var todayExits = await sessionsQuery
            .CountAsync(s => s.ExitTime >= todayStart && s.ExitTime < periodEnd, ct);

        var periodEntries = await sessionsQuery
            .CountAsync(s => s.EntryTime >= periodStart && s.EntryTime < periodEnd, ct);
        var exitedInPeriod = await sessionsQuery
            .Where(s => s.ExitTime >= periodStart && s.ExitTime < periodEnd)
            .Select(s => new { s.EntryTime, ExitTime = s.ExitTime!.Value })
            .ToListAsync(ct);
        var periodExits = exitedInPeriod.Count;
        var averageDurationMinutes = exitedInPeriod.Count == 0
            ? 0d
            : exitedInPeriod.Average(s => Math.Max(0d, (s.ExitTime - s.EntryTime).TotalMinutes));

        var payments = await (
            from payment in _db.Payments.AsNoTracking()
            join session in _db.ParkingSessions.AsNoTracking()
                on payment.ParkingSessionId equals session.Id
            where (!parkingLocationId.HasValue || session.ParkingLocationId == parkingLocationId.Value)
                  && ((payment.CreatedAt >= previousPeriodStart && payment.CreatedAt < periodEnd)
                      || (payment.PaidAt >= previousPeriodStart && payment.PaidAt < periodEnd))
            select payment)
            .Select(p => new PaymentRow(p.Provider, p.Status, p.Amount, p.PaidAt, p.CreatedAt))
            .ToListAsync(ct);

        var currentPayments = payments
            .Where(p => (p.CreatedAt >= periodStart && p.CreatedAt < periodEnd)
                        || (p.PaidAt >= periodStart && p.PaidAt < periodEnd))
            .ToArray();

        var paidPayments = currentPayments
            .Where(p => p.Status == PaymentStatus.Paid
                        && p.PaidAt >= periodStart && p.PaidAt < periodEnd)
            .ToArray();
        var previousPeriodRevenue = payments
            .Where(p => p.Status == PaymentStatus.Paid
                        && p.PaidAt >= previousPeriodStart && p.PaidAt < periodStart)
            .Sum(p => p.Amount);

        var todayRevenue = paidPayments
            .Where(p => p.PaidAt >= todayStart && p.PaidAt < periodEnd)
            .Sum(p => p.Amount);

        var revenue = Enumerable.Range(0, periodDays)
            .Select(offset =>
            {
                var date = periodStart.AddDays(offset);
                var dayEnd = date.AddDays(1);
                var dayPayments = paidPayments
                    .Where(p => p.PaidAt >= date && p.PaidAt < dayEnd)
                    .ToArray();
                return new RevenuePointResponse(date, dayPayments.Sum(p => p.Amount), dayPayments.Length);
            })
            .ToArray();

        var complimentaryCount = await sessionsQuery
            .CountAsync(s =>
                s.EntryTime >= periodStart && s.EntryTime < periodEnd
                && s.FeeOverride == 0m
                && s.Status != ParkingSessionStatus.Void
                && s.Status != ParkingSessionStatus.Cancelled, ct);
        var supervisorOverrides = await _db.AuditLogs.AsNoTracking()
            .CountAsync(a => a.EntityType == nameof(ParkingSession)
                             && PaymentOverrideActions.Contains(a.Action)
                             && a.CreatedAt >= periodStart
                             && a.CreatedAt < periodEnd
                             && (!parkingLocationId.HasValue || a.ParkingLocationId == parkingLocationId.Value), ct);

        var paymentMix = new[]
        {
            Mix("paymongo", "PayMongo", paidPayments.Count(p => p.Provider == PaymentProvider.PayMongo), paidPayments.Where(p => p.Provider == PaymentProvider.PayMongo).Sum(p => p.Amount)),
            Mix("cash", "Cash", paidPayments.Count(p => p.Provider == PaymentProvider.Cash), paidPayments.Where(p => p.Provider == PaymentProvider.Cash).Sum(p => p.Amount)),
            Mix("complimentary", "Complimentary", complimentaryCount, 0m),
            Mix("failed", "Failed / expired", currentPayments.Count(p => p.Status is PaymentStatus.Failed or PaymentStatus.Expired), currentPayments.Where(p => p.Status is PaymentStatus.Failed or PaymentStatus.Expired).Sum(p => p.Amount)),
            Mix("pending", "Pending", currentPayments.Count(p => p.Status is PaymentStatus.Pending or PaymentStatus.Processing), currentPayments.Where(p => p.Status is PaymentStatus.Pending or PaymentStatus.Processing).Sum(p => p.Amount))
        };

        var periodRevenue = paidPayments.Sum(p => p.Amount);

        var summary = new DashboardSummaryResponse(
            activeSessions,
            paidAwaitingExit,
            unpaidSessions,
            overGraceSessions,
            overGraceAmount,
            todayEntries,
            todayExits,
            todayRevenue,
            Currency,
            periodEntries,
            periodExits,
            periodRevenue,
            averageDurationMinutes,
            previousPeriodRevenue,
            supervisorOverrides);

        return new DashboardReportResponse(periodStart, periodEnd, summary, revenue, paymentMix);
    }

    private static PaymentMixResponse Mix(string key, string label, int count, decimal amount)
        => new(key, label, amount, count);

    private sealed record PaymentRow(
        PaymentProvider Provider,
        PaymentStatus Status,
        decimal Amount,
        DateTimeOffset? PaidAt,
        DateTimeOffset CreatedAt);
}

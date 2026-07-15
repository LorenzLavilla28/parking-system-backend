using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
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

    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;

    public DashboardReportService(IApplicationDbContext db, IDateTime clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<DashboardReportResponse> GetAsync(int days, CancellationToken ct)
    {
        var normalizedDays = Math.Clamp(days, 1, 31);
        var now = _clock.UtcNow;
        var todayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var periodStart = todayStart.AddDays(-(normalizedDays - 1));
        var periodEnd = todayStart.AddDays(1);

        var statusCounts = await _db.ParkingSessions
            .AsNoTracking()
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var countByStatus = statusCounts.ToDictionary(x => x.Status, x => x.Count);
        var unpaidSessions = countByStatus.GetValueOrDefault(ParkingSessionStatus.ActiveUnpaid)
            + countByStatus.GetValueOrDefault(ParkingSessionStatus.PaymentPending);
        var paidAwaitingExit = await _db.ParkingSessions
            .AsNoTracking()
            .CountAsync(s => s.Status == ParkingSessionStatus.PaidExitWindow
                             && (s.PaidExitDeadline == null || s.PaidExitDeadline > now), ct);
        var persistedOverdue = countByStatus.GetValueOrDefault(ParkingSessionStatus.OverstayDue);
        var staleOverdue = await _db.ParkingSessions
            .AsNoTracking()
            .CountAsync(s => s.Status == ParkingSessionStatus.PaidExitWindow
                             && s.PaidExitDeadline != null && s.PaidExitDeadline <= now, ct);
        var overGraceSessions = persistedOverdue + staleOverdue;
        var activeSessions = unpaidSessions + paidAwaitingExit + overGraceSessions;

        var todayEntries = await _db.ParkingSessions
            .AsNoTracking()
            .CountAsync(s => s.EntryTime >= todayStart && s.EntryTime < periodEnd, ct);
        var todayExits = await _db.ParkingSessions
            .AsNoTracking()
            .CountAsync(s => s.ExitTime >= todayStart && s.ExitTime < periodEnd, ct);

        var payments = await _db.Payments
            .AsNoTracking()
            .Where(p =>
                (p.CreatedAt >= periodStart && p.CreatedAt < periodEnd)
                || (p.PaidAt >= periodStart && p.PaidAt < periodEnd))
            .Select(p => new PaymentRow(p.Provider, p.Status, p.Amount, p.PaidAt, p.CreatedAt))
            .ToListAsync(ct);

        var paidPayments = payments
            .Where(p => p.Status == PaymentStatus.Paid && p.PaidAt is not null)
            .ToArray();

        var todayRevenue = paidPayments
            .Where(p => p.PaidAt >= todayStart && p.PaidAt < periodEnd)
            .Sum(p => p.Amount);

        var revenue = Enumerable.Range(0, normalizedDays)
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

        var complimentaryCount = await _db.ParkingSessions
            .AsNoTracking()
            .CountAsync(s =>
                s.EntryTime >= periodStart && s.EntryTime < periodEnd
                && s.FeeOverride == 0m
                && s.Status != ParkingSessionStatus.Void
                && s.Status != ParkingSessionStatus.Cancelled, ct);

        var paymentMix = new[]
        {
            Mix("paymongo", "PayMongo", paidPayments.Count(p => p.Provider == PaymentProvider.PayMongo), paidPayments.Where(p => p.Provider == PaymentProvider.PayMongo).Sum(p => p.Amount)),
            Mix("cash", "Cash", paidPayments.Count(p => p.Provider == PaymentProvider.Cash), paidPayments.Where(p => p.Provider == PaymentProvider.Cash).Sum(p => p.Amount)),
            Mix("complimentary", "Complimentary", complimentaryCount, 0m),
            Mix("failed", "Failed", payments.Count(p => p.Status is PaymentStatus.Failed or PaymentStatus.Expired or PaymentStatus.Cancelled), payments.Where(p => p.Status is PaymentStatus.Failed or PaymentStatus.Expired or PaymentStatus.Cancelled).Sum(p => p.Amount)),
            Mix("pending", "Pending", payments.Count(p => p.Status is PaymentStatus.Pending or PaymentStatus.Processing), payments.Where(p => p.Status is PaymentStatus.Pending or PaymentStatus.Processing).Sum(p => p.Amount))
        };

        var summary = new DashboardSummaryResponse(
            activeSessions,
            paidAwaitingExit,
            unpaidSessions,
            overGraceSessions,
            todayEntries,
            todayExits,
            todayRevenue,
            Currency);

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

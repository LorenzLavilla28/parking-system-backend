using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Pricing;
using ParkingSaaS.Contracts.Reports;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Tenants;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Application.Reports;

public sealed class OperationsSummaryService : IOperationsSummaryService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTime _clock;
    private readonly ISessionPricingService _pricing;
    private readonly ITenantContext _tenant;
    private readonly IEmailQueue _emailQueue;
    private readonly EmailOptions _emailOptions;

    public OperationsSummaryService(
        IApplicationDbContext db,
        IDateTime clock,
        ISessionPricingService pricing,
        ITenantContext tenant,
        IEmailQueue emailQueue,
        IOptions<EmailOptions> emailOptions)
    {
        _db = db;
        _clock = clock;
        _pricing = pricing;
        _tenant = tenant;
        _emailQueue = emailQueue;
        _emailOptions = emailOptions.Value;
    }

    public Task<OperationsSummaryResponse> GetCurrentAsync(int hours, CancellationToken ct)
    {
        var tenantId = _tenant.TenantId;
        if (tenantId == Guid.Empty)
            throw new NotFoundException("Tenant context not found.");

        var end = _clock.UtcNow;
        return BuildAsync(tenantId, end.AddHours(-NormalizeHours(hours)), end, ct);
    }

    public async Task<OperationsSummaryEmailResponse> QueueCurrentEmailAsync(int hours, CancellationToken ct)
    {
        var tenantId = _tenant.TenantId;
        if (tenantId == Guid.Empty)
            throw new NotFoundException("Tenant context not found.");

        var end = _clock.UtcNow;
        var start = end.AddHours(-NormalizeHours(hours));
        var recipients = await QueueForTenantAsync(tenantId, start, end, ct);
        await _db.SaveChangesAsync(ct);
        return new OperationsSummaryEmailResponse(recipients, start, end);
    }

    public async Task<int> QueueScheduledEmailsAsync(int hours, CancellationToken ct)
    {
        var end = _clock.UtcNow;
        var start = end.AddHours(-NormalizeHours(hours));
        var tenantIds = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Status == TenantStatus.Active)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var queued = 0;
        foreach (var tenantId in tenantIds)
            queued += await QueueForTenantAsync(tenantId, start, end, ct);

        if (queued > 0)
            await _db.SaveChangesAsync(ct);
        return queued;
    }

    private async Task<int> QueueForTenantAsync(Guid tenantId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var admins = await _db.Users.IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId
                        && u.Status == UserStatus.Active
                        && u.Roles.Any(r => r.Role == RoleType.TenantAdministrator))
            .Select(u => new { u.Email, u.FirstName, u.LastName })
            .ToListAsync(ct);
        if (admins.Count == 0)
            return 0;

        var summary = await BuildAsync(tenantId, start, end, ct);
        var dashboardUrl = $"{_emailOptions.AppBaseUrl.TrimEnd('/')}/admin/reports";
        foreach (var admin in admins)
        {
            _emailQueue.QueueOperationsSummary(
                tenantId,
                admin.Email,
                $"{admin.FirstName} {admin.LastName}".Trim(),
                new OperationsSummaryEmailData(summary, dashboardUrl),
                end);
        }

        return admins.Count;
    }

    private async Task<OperationsSummaryResponse> BuildAsync(
        Guid tenantId, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new NotFoundException("Tenant not found.");

        var entries = await _db.ParkingSessions.IgnoreQueryFilters()
            .CountAsync(s => s.TenantId == tenantId && s.EntryTime >= start && s.EntryTime < end, ct);
        var exits = await _db.ParkingSessions.IgnoreQueryFilters()
            .CountAsync(s => s.TenantId == tenantId && s.ExitTime >= start && s.ExitTime < end, ct);
        var active = await _db.ParkingSessions.IgnoreQueryFilters()
            .CountAsync(s => s.TenantId == tenantId && (
                s.Status == ParkingSessionStatus.ActiveUnpaid
                || s.Status == ParkingSessionStatus.PaymentPending
                || s.Status == ParkingSessionStatus.PaidExitWindow
                || s.Status == ParkingSessionStatus.OverstayDue), ct);
        var overstayCandidates = await _db.ParkingSessions.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId
                        && (s.Status == ParkingSessionStatus.OverstayDue
                            || (s.Status == ParkingSessionStatus.PaidExitWindow
                                && s.PaidExitDeadline != null
                                && s.PaidExitDeadline <= end)))
            .ToListAsync(ct);
        var currentOverstays = 0;
        foreach (var session in overstayCandidates)
        {
            if (session.EffectiveStatus(end) != ParkingSessionStatus.OverstayDue)
                continue;

            var calculation = await _pricing.CalculateAsync(session, end, discount: null, ct);
            // A payment can settle an overstay without the lifecycle status being
            // refreshed before this report runs. Recalculate the live balance so
            // fully paid sessions are not reported as requiring attention.
            if (calculation is null || session.Outstanding(calculation.TotalAmount) > 0m)
                currentOverstays++;
        }

        var payments = await _db.Payments.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId
                        && ((p.CreatedAt >= start && p.CreatedAt < end)
                            || (p.PaidAt >= start && p.PaidAt < end)))
            .Select(p => new PaymentRow(p.Status, p.Amount, p.PaidAt))
            .ToListAsync(ct);

        var paid = payments.Where(p => p.Status == PaymentStatus.Paid && p.PaidAt >= start && p.PaidAt < end).ToArray();
        var pending = payments.Where(p => p.Status is PaymentStatus.Pending or PaymentStatus.Processing).ToArray();
        var failed = payments.Where(p => p.Status is PaymentStatus.Failed or PaymentStatus.Expired or PaymentStatus.Cancelled).ToArray();
        var failedWebhooks = await _db.WebhookEvents.IgnoreQueryFilters()
            .CountAsync(w => w.TenantId == tenantId
                             && w.ReceivedAt >= start && w.ReceivedAt < end
                             && w.ProcessingStatus == WebhookProcessingStatus.Failed, ct);

        var attention = new List<OperationsAttentionItem>();
        if (currentOverstays > 0)
            attention.Add(new("danger", "Overstay sessions", $"{currentOverstays} session(s) passed the paid exit window."));
        if (pending.Length > 0)
            attention.Add(new("warning", "Pending payments", $"{pending.Length} payment(s) are awaiting provider confirmation."));
        if (failed.Length > 0)
            attention.Add(new("warning", "Failed or closed payments", $"{failed.Length} payment attempt(s) need review."));
        if (failedWebhooks > 0)
            attention.Add(new("danger", "Failed provider webhooks", $"{failedWebhooks} webhook event(s) failed processing."));

        return new OperationsSummaryResponse(
            tenant.Id,
            tenant.Name,
            tenant.DefaultCurrency,
            tenant.DefaultTimezone,
            start,
            end,
            end,
            entries,
            exits,
            active,
            paid.Sum(p => p.Amount),
            currentOverstays,
            pending.Length,
            pending.Sum(p => p.Amount),
            failed.Length,
            failed.Sum(p => p.Amount),
            failedWebhooks,
            new[]
            {
                new OperationsPaymentBreakdown("Successful", paid.Length, paid.Sum(p => p.Amount)),
                new OperationsPaymentBreakdown("Pending", pending.Length, pending.Sum(p => p.Amount)),
                new OperationsPaymentBreakdown("Failed / closed", failed.Length, failed.Sum(p => p.Amount))
            },
            attention);
    }

    private static int NormalizeHours(int hours) => Math.Clamp(hours, 1, 24);

    private sealed record PaymentRow(PaymentStatus Status, decimal Amount, DateTimeOffset? PaidAt);
}

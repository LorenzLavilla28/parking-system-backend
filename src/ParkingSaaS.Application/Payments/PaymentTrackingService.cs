using System.Text;
using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Payments;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Application.Payments;

public sealed class PaymentTrackingService : IPaymentTrackingService
{
    private readonly IApplicationDbContext _db;

    public PaymentTrackingService(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<PaymentSummaryResponse>> SearchAsync(PaymentQueryRequest request, CancellationToken ct)
    {
        var query = BuildQuery(request);
        var total = await query.LongCountAsync(ct);
        var payments = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((request.NormalizedPage - 1) * request.NormalizedPageSize)
            .Take(request.NormalizedPageSize)
            .ToListAsync(ct);
        var rows = await LoadProjectionsAsync(payments, ct);

        return new PagedResult<PaymentSummaryResponse>(
            rows.Select(ToContract).ToArray(), request.NormalizedPage, request.NormalizedPageSize, total);
    }

    public async Task<PaymentDetailResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var payment = await _db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Payment not found.");
        var session = await _db.ParkingSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == payment.ParkingSessionId, ct)
            ?? throw new NotFoundException("Parking session not found.");
        var locationName = await _db.ParkingLocations.AsNoTracking()
            .Where(l => l.Id == session.ParkingLocationId)
            .Select(l => l.Name)
            .FirstOrDefaultAsync(ct) ?? "Unknown location";
        var quote = payment.FeeQuoteId is { } quoteId
            ? await _db.FeeQuotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            : null;

        var summary = ToContract(new PaymentProjection(
            payment.Id, payment.ParkingSessionId, session.ParkingLocationId, locationName,
            session.PlateNumberRaw, payment.Status, payment.Provider, payment.PaymentMethod,
            payment.Amount, payment.Currency, payment.CreatedAt, payment.PaidAt,
            payment.ReceiptNumber, payment.ProviderCheckoutSessionId, payment.ProviderPaymentId,
            payment.CustomerEmail, payment.RecordedByGuardId, session.Status, session.EntryTime,
            session.ExitTime, session.FinalFee, session.TotalPaid, session.PaidExitDeadline));

        var audit = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == nameof(Payment) && a.EntityId == id.ToString())
            .OrderBy(a => a.CreatedAt)
            .Select(a => new PaymentAuditResponse(
                a.Id, a.CreatedAt, a.Action, a.EntityType, a.EntityId, a.UserId,
                a.OldValuesJson, a.NewValuesJson, a.Reason, a.IpAddress, a.DeviceInformation))
            .ToListAsync(ct);

        var webhooks = await _db.WebhookEvents.AsNoTracking()
            .Where(e => e.PaymentId == id)
            .OrderBy(e => e.ReceivedAt)
            .Select(e => new PaymentWebhookResponse(
                e.Id, e.Provider.ToString(), e.ProviderEventId, e.EventType, e.PayloadHash,
                e.PaymentId, e.ReceivedAt, e.ProcessedAt, e.ProcessingStatus.ToString(), e.FailureReason))
            .ToListAsync(ct);

        var timeline = BuildTimeline(payment, session, audit, webhooks);
        var sessionContext = new PaymentSessionContext(
            session.Id, session.PlateNumberRaw, session.VehicleType.ToString(), locationName,
            session.EntryTime, session.ExitTime, EffectiveSessionStatus(session.Status, session.PaidExitDeadline).ToString(), session.FinalFee,
            session.TotalPaid, session.PaidExitDeadline);
        var quoteContext = quote is null
            ? null
            : new PaymentQuoteContext(
                quote.Id, quote.BaseAmount, quote.DiscountAmount, quote.TotalAmount, quote.Currency,
                quote.CreatedAt, quote.ExpiresAt, quote.Status.ToString(), quote.PricingBreakdownJson);

        return new PaymentDetailResponse(summary, sessionContext, quoteContext, timeline, audit, webhooks);
    }

    public async Task<byte[]> ExportCsvAsync(PaymentQueryRequest request, CancellationToken ct)
    {
        var payments = await BuildQuery(request)
            .OrderByDescending(x => x.CreatedAt)
            .Take(10_000)
            .ToListAsync(ct);
        var rows = await LoadProjectionsAsync(payments, ct);

        var sb = new StringBuilder();
        sb.AppendLine("Payment ID,Created At,Paid At,Plate,Location,Amount,Currency,Provider,Method,Status,Receipt Number,Provider Checkout ID,Provider Payment ID,Session Status,Entry Time,Exit Time,Final Fee,Total Paid");
        foreach (var row in rows.Select(ToContract))
        {
            sb.AppendLine(string.Join(',',
                Csv(row.Id), Csv(row.CreatedAt), Csv(row.PaidAt), Csv(row.PlateNumberRaw), Csv(row.LocationName),
                Csv(row.Amount), Csv(row.Currency), Csv(row.Provider), Csv(row.PaymentMethod), Csv(row.Status),
                Csv(row.ReceiptNumber), Csv(row.ProviderCheckoutSessionId), Csv(row.ProviderPaymentId),
                Csv(row.SessionStatus), Csv(row.EntryTime), Csv(row.ExitTime), Csv(row.FinalFee), Csv(row.TotalPaid)));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private IQueryable<Payment> BuildQuery(PaymentQueryRequest request)
    {
        // Keep filtering, sorting, and paging on the Payment entity. EF Core
        // cannot translate ordering by a property of a constructor-projected
        // record (the previous implementation produced a 500 on PostgreSQL).
        var query = _db.Payments.AsNoTracking();

        if (request.From is { } from)
            query = query.Where(x => (x.PaidAt ?? x.CreatedAt) >= from);
        if (request.To is { } to)
            query = query.Where(x => (x.PaidAt ?? x.CreatedAt) < to);
        if (request.LocationId is { } locationId)
            query = query.Where(p => _db.ParkingSessions.Any(s =>
                s.Id == p.ParkingSessionId && s.ParkingLocationId == locationId));
        if (request.SessionId is { } sessionId)
            query = query.Where(p => p.ParkingSessionId == sessionId);
        if (Enum.TryParse<PaymentStatus>(request.Status, true, out var status))
            query = query.Where(p => p.Status == status);
        if (Enum.TryParse<PaymentProvider>(request.Provider, true, out var provider))
            query = query.Where(p => p.Provider == provider);
        if (!string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            var method = request.PaymentMethod.Trim();
            query = query.Where(p => p.PaymentMethod == method);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            var hasGuid = Guid.TryParse(search, out var paymentId);
            var sessionIds = _db.ParkingSessions
                .Where(s => s.PlateNumberRaw.Contains(search))
                .Select(s => s.Id);
            query = query.Where(p => sessionIds.Contains(p.ParkingSessionId)
                || (p.ReceiptNumber != null && p.ReceiptNumber.Contains(search))
                || (p.ProviderPaymentId != null && p.ProviderPaymentId.Contains(search))
                || (p.ProviderCheckoutSessionId != null && p.ProviderCheckoutSessionId.Contains(search))
                || (hasGuid && p.Id == paymentId));
        }

        // A payment without its session/location cannot be investigated in this
        // view, so preserve the old inner-join behavior without projecting a
        // non-translatable DTO in SQL.
        return query.Where(p => _db.ParkingSessions.Any(s => s.Id == p.ParkingSessionId
            && _db.ParkingLocations.Any(l => l.Id == s.ParkingLocationId)));
    }

    private async Task<IReadOnlyList<PaymentProjection>> LoadProjectionsAsync(
        IReadOnlyList<Payment> payments, CancellationToken ct)
    {
        var sessionIds = payments.Select(p => p.ParkingSessionId).Distinct().ToArray();
        if (sessionIds.Length == 0)
            return Array.Empty<PaymentProjection>();

        var sessions = await _db.ParkingSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        var locationIds = sessions.Values.Select(s => s.ParkingLocationId).Distinct().ToArray();
        var locations = await _db.ParkingLocations.AsNoTracking()
            .Where(l => locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, ct);

        return payments
            .Where(p => sessions.ContainsKey(p.ParkingSessionId))
            .Select(p =>
            {
                var session = sessions[p.ParkingSessionId];
                var locationName = locations.TryGetValue(session.ParkingLocationId, out var location)
                    ? location.Name
                    : "Unknown location";
                return new PaymentProjection(
                    p.Id, p.ParkingSessionId, session.ParkingLocationId, locationName,
                    session.PlateNumberRaw, p.Status, p.Provider, p.PaymentMethod,
                    p.Amount, p.Currency, p.CreatedAt, p.PaidAt, p.ReceiptNumber,
                    p.ProviderCheckoutSessionId, p.ProviderPaymentId, p.CustomerEmail,
                    p.RecordedByGuardId, session.Status, session.EntryTime, session.ExitTime,
                    session.FinalFee, session.TotalPaid, session.PaidExitDeadline);
            })
            .ToArray();
    }

    private static PaymentSummaryResponse ToContract(PaymentProjection p) => new(
        p.Id, p.ParkingSessionId, p.ParkingLocationId, p.LocationName, p.PlateNumberRaw,
        p.Status.ToString(), p.Provider.ToString(), p.PaymentMethod, p.Amount, p.Currency,
        p.CreatedAt, p.PaidAt, p.ReceiptNumber, p.ProviderCheckoutSessionId, p.ProviderPaymentId,
        MaskEmail(p.CustomerEmail), p.RecordedByGuardId, EffectiveSessionStatus(p.SessionStatus, p.PaidExitDeadline).ToString(), p.EntryTime,
        p.ExitTime, p.FinalFee, p.TotalPaid, p.PaidExitDeadline);

    private static ParkingSessionStatus EffectiveSessionStatus(ParkingSessionStatus status, DateTimeOffset? deadline)
        => status == ParkingSessionStatus.PaidExitWindow && deadline is { } value && value <= DateTimeOffset.UtcNow
            ? ParkingSessionStatus.OverstayDue
            : status;

    private static IReadOnlyList<PaymentTimelineItem> BuildTimeline(
        Payment payment, ParkingSession session, IReadOnlyList<PaymentAuditResponse> audit,
        IReadOnlyList<PaymentWebhookResponse> webhooks)
    {
        var events = new List<PaymentTimelineItem>
        {
            new(payment.CreatedAt, "payment", "Payment record created", $"{payment.Provider} payment attempt", payment.Status.ToString())
        };

        if (payment.PaidAt is { } paidAt)
            events.Add(new(paidAt, "paid", "Payment confirmed", $"{payment.Amount:0.00} {payment.Currency} via {payment.PaymentMethod ?? payment.Provider.ToString()}", payment.Status.ToString()));
        if (payment.Status is PaymentStatus.Failed or PaymentStatus.Expired or PaymentStatus.Cancelled)
            events.Add(new(payment.UpdatedAt, "closed", $"Payment {payment.Status}", null, payment.Status.ToString()));
        if (session.ExitTime is { } exitTime)
            events.Add(new(exitTime, "exit", "Session exited", $"Final fee {session.FinalFee:0.00}", session.Status.ToString()));

        events.AddRange(audit.Select(a => new PaymentTimelineItem(
            a.CreatedAt, "audit", a.Action, a.Reason ?? a.DeviceInformation ?? a.IpAddress, null)));
        events.AddRange(webhooks.Select(w => new PaymentTimelineItem(
            w.ReceivedAt, "webhook", $"Webhook {w.ProcessingStatus}",
            $"{w.EventType} / {w.ProviderEventId}", w.ProcessingStatus)));

        return events.OrderBy(e => e.At).ToArray();
    }

    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.IndexOf('@');
        if (at <= 1) return "•••" + (at > 0 ? email[at..] : string.Empty);
        return email[0] + "•••" + email[(at - 1)..];
    }

    private static string Csv(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTimeOffset date => date.ToString("O"),
            decimal amount => amount.ToString("0.00"),
            _ => value.ToString() ?? string.Empty
        };
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private sealed record PaymentProjection(
        Guid Id,
        Guid ParkingSessionId,
        Guid ParkingLocationId,
        string LocationName,
        string PlateNumberRaw,
        PaymentStatus Status,
        PaymentProvider Provider,
        string? PaymentMethod,
        decimal Amount,
        string Currency,
        DateTimeOffset CreatedAt,
        DateTimeOffset? PaidAt,
        string? ReceiptNumber,
        string? ProviderCheckoutSessionId,
        string? ProviderPaymentId,
        string? CustomerEmail,
        Guid? RecordedByGuardId,
        ParkingSessionStatus SessionStatus,
        DateTimeOffset EntryTime,
        DateTimeOffset? ExitTime,
        decimal? FinalFee,
        decimal TotalPaid,
        DateTimeOffset? PaidExitDeadline);
}

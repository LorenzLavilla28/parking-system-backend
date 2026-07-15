using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Guard;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Services;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.Application.Guard;

/// <summary>
/// Read-side operations for guards over parking sessions: search by plate,
/// fetch one, and reprint a session's QR. Plain guards see only sessions at
/// their assigned locations; supervisors and admins see the whole tenant.
/// </summary>
public sealed class GuardSessionService : IGuardSessionService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPlateNormalizer _plateNormalizer;
    private readonly IParkingTokenService _tokens;
    private readonly IQrCodeGenerator _qr;
    private readonly PublicUrlOptions _urls;

    public GuardSessionService(
        IApplicationDbContext db,
        ICurrentUser user,
        IPlateNormalizer plateNormalizer,
        IParkingTokenService tokens,
        IQrCodeGenerator qr,
        IOptions<PublicUrlOptions> urls)
    {
        _db = db;
        _user = user;
        _plateNormalizer = plateNormalizer;
        _tokens = tokens;
        _qr = qr;
        _urls = urls.Value;
    }

    public async Task<PagedResult<SessionSummaryResponse>> SearchAsync(
        string? plate, string? status, Guid? parkingLocationId, bool activeOnly, PageQuery page, CancellationToken ct)
    {
        var q = _db.ParkingSessions.AsNoTracking().AsQueryable();

        q = await ApplyLocationScopeAsync(q, parkingLocationId, ct);

        if (!string.IsNullOrWhiteSpace(plate))
        {
            var normalized = _plateNormalizer.Normalize(plate);
            q = q.Where(s => s.PlateNumberNormalized.StartsWith(normalized));
        }

        if (activeOnly)
        {
            q = q.Where(s =>
                s.Status == ParkingSessionStatus.ActiveUnpaid ||
                s.Status == ParkingSessionStatus.PaymentPending ||
                s.Status == ParkingSessionStatus.PaidExitWindow ||
                s.Status == ParkingSessionStatus.OverstayDue);
        }
        else if (!string.IsNullOrWhiteSpace(status) &&
                 Enum.TryParse<ParkingSessionStatus>(status, ignoreCase: true, out var parsed))
        {
            q = q.Where(s => s.Status == parsed);
        }

        q = q.OrderByDescending(s => s.EntryTime);
        var now = DateTimeOffset.UtcNow;

        var total = await q.LongCountAsync(ct);
        var items = await q
            .Skip((page.NormalizedPage - 1) * page.NormalizedPageSize)
            .Take(page.NormalizedPageSize)
            .ToListAsync(ct);

        var locationNames = await _db.ParkingLocations
            .Where(l => items.Select(s => s.ParkingLocationId).Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct);

        return new PagedResult<SessionSummaryResponse>(
            items.Select(s => ToSummary(s, locationNames.GetValueOrDefault(s.ParkingLocationId) ?? "Unknown location", now)).ToArray(),
            page.NormalizedPage,
            page.NormalizedPageSize,
            total);
    }

    public async Task<SessionSummaryResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var session = await FindAccessibleAsync(id, ct);
        var locationName = await _db.ParkingLocations
            .Where(l => l.Id == session.ParkingLocationId)
            .Select(l => l.Name)
            .FirstOrDefaultAsync(ct) ?? "Unknown location";
        return ToSummary(session, locationName, DateTimeOffset.UtcNow);
    }

    public async Task<SessionQrResponse> GetQrAsync(Guid id, CancellationToken ct)
    {
        var session = await FindAccessibleAsync(id, ct);

        // Recover the original token/code to rebuild the exact same QR.
        var publicToken = _tokens.Unprotect(session.PublicTokenProtected);
        var ticketCode = _tokens.Unprotect(session.TicketCodeProtected);
        var paymentUrl = _urls.SessionPath(publicToken);

        return new SessionQrResponse(session.Id, ticketCode, paymentUrl, _qr.GeneratePngDataUri(paymentUrl));
    }

    private async Task<ParkingSession> FindAccessibleAsync(Guid id, CancellationToken ct)
    {
        var session = await _db.ParkingSessions.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Parking session not found.");

        var assigned = await AssignedLocationIdsOrNullAsync(ct);
        if (assigned is not null && !assigned.Contains(session.ParkingLocationId))
            throw new ForbiddenException("You are not assigned to this parking location.");

        return session;
    }

    private async Task<IQueryable<ParkingSession>> ApplyLocationScopeAsync(
        IQueryable<ParkingSession> q, Guid? requestedLocationId, CancellationToken ct)
    {
        var assigned = await AssignedLocationIdsOrNullAsync(ct);
        if (assigned is not null)
            q = q.Where(s => assigned.Contains(s.ParkingLocationId));

        if (requestedLocationId.HasValue)
            q = q.Where(s => s.ParkingLocationId == requestedLocationId.Value);

        return q;
    }

    /// <summary>Returns the guard's assigned location ids, or null for supervisors/admins (no restriction).</summary>
    private async Task<HashSet<Guid>?> AssignedLocationIdsOrNullAsync(CancellationToken ct)
    {
        if (_user.Roles.Contains(RoleType.Supervisor) || _user.Roles.Contains(RoleType.TenantAdministrator))
            return null;

        var ids = await _db.UserParkingLocations
            .Where(a => a.UserId == _user.UserId)
            .Select(a => a.ParkingLocationId)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    private static SessionSummaryResponse ToSummary(ParkingSession s, string locationName, DateTimeOffset now) => new(
        s.Id,
        s.ParkingLocationId,
        locationName,
        s.PlateNumberRaw,
        s.VehicleType.ToString(),
        s.VehicleColor,
        s.EntryTime,
        s.EffectiveStatus(now).ToString(),
        s.FinalFee,
        s.TotalPaid,
        s.PaidExitDeadline);
}

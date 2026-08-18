using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Pricing;
using ParkingSaaS.Contracts.Guard;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Services;

namespace ParkingSaaS.Application.Guard;

/// <summary>
/// Records a vehicle entry: normalizes the plate, prevents a duplicate active
/// session at the same location, mints the public token + ticket code, and
/// returns a printable QR ticket. The raw token/code are returned once and never
/// stored in plaintext.
/// </summary>
public sealed class GuardEntryService : IGuardEntryService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPlateNormalizer _plateNormalizer;
    private readonly IParkingTokenService _tokens;
    private readonly IQrCodeGenerator _qr;
    private readonly IActiveRatePlanResolver _ratePlanResolver;
    private readonly IDateTime _clock;
    private readonly ISessionRealtimeNotifier _realtime;
    private readonly PublicUrlOptions _urls;
    private readonly ILogger<GuardEntryService> _logger;

    public GuardEntryService(
        IApplicationDbContext db,
        ICurrentUser user,
        IPlateNormalizer plateNormalizer,
        IParkingTokenService tokens,
        IQrCodeGenerator qr,
        IActiveRatePlanResolver ratePlanResolver,
        IDateTime clock,
        ISessionRealtimeNotifier realtime,
        IOptions<PublicUrlOptions> urls,
        ILogger<GuardEntryService> logger)
    {
        _db = db;
        _user = user;
        _plateNormalizer = plateNormalizer;
        _tokens = tokens;
        _qr = qr;
        _ratePlanResolver = ratePlanResolver;
        _clock = clock;
        _realtime = realtime;
        _urls = urls.Value;
        _logger = logger;
    }

    public async Task<EntryTicketResponse> RecordEntryAsync(RecordEntryRequest request, CancellationToken ct)
    {
        await GuardLocationAccess.EnsureCanOperateAsync(_db, _user, request.ParkingLocationId, ct);

        if (!Enum.TryParse<VehicleType>(request.VehicleType, ignoreCase: true, out var vehicleType))
            throw new ConflictException($"Unknown vehicle type '{request.VehicleType}'.");

        var normalized = _plateNormalizer.Normalize(request.PlateNumber);
        if (string.IsNullOrEmpty(normalized))
            throw new ConflictException("Plate number is empty after normalization.");

        ParkingLocation location = null!;
        ParkingSession session = null!;
        string publicToken = string.Empty;
        string ticketCode = string.Empty;

        await _db.ExecuteInTransactionAsync(async txct =>
        {
            // Serialize entry reservations at this location. The partial unique
            // index still protects the duplicate-plate invariant.
            await _db.LockLocationAsync(request.ParkingLocationId, txct);

            var duplicate = await _db.ParkingSessions.AnyAsync(s =>
                s.ParkingLocationId == request.ParkingLocationId &&
                s.PlateNumberNormalized == normalized &&
                (s.Status == ParkingSessionStatus.ActiveUnpaid ||
                 s.Status == ParkingSessionStatus.PaymentPending ||
                 s.Status == ParkingSessionStatus.PaidExitWindow ||
                 s.Status == ParkingSessionStatus.OverstayDue), txct);
            if (duplicate)
                throw new ConflictException("An active session already exists for this plate at this location.");

            location = await _db.ParkingLocations.FirstAsync(l => l.Id == request.ParkingLocationId, txct);

            if (location.ActiveRatePlanId is null)
                throw new ConflictException("rate_plan_required: assign an active rate plan before accepting vehicle entries.");

            var activeCount = await _db.ParkingSessions.CountAsync(s =>
                s.ParkingLocationId == location.Id &&
                (s.Status == ParkingSessionStatus.ActiveUnpaid ||
                 s.Status == ParkingSessionStatus.PaymentPending ||
                 s.Status == ParkingSessionStatus.PaidExitWindow ||
                 s.Status == ParkingSessionStatus.OverstayDue), txct);
            if (activeCount >= location.SlotCapacity)
                throw new ConflictException($"location_at_capacity: {location.Name} is full ({location.SlotCapacity} slots).");

            session = ParkingSession.RecordEntry(
                tenantId: location.TenantId,
                parkingLocationId: location.Id,
                createdByGuardId: _user.UserId ?? Guid.Empty,
                plateRaw: request.PlateNumber,
                plateNormalized: normalized,
                vehicleType: vehicleType,
                notes: request.Notes,
                entryTime: _clock.UtcNow,
                entryPhotoUrl: request.EntryPhotoUrl);

            // Pin the rate plan version in effect at entry so later edits to the plan
            // never change this session's pricing terms.
            var versionId = await _ratePlanResolver.ResolveActiveVersionIdAsync(location.Id, session.EntryTime, txct);
            if (versionId is not { } v)
                throw new ConflictException("rate_plan_required: the location must have an active rate plan version before accepting entries.");
            session.SetRatePlanVersion(v);

            publicToken = _tokens.GeneratePublicToken();
            ticketCode = _tokens.GenerateTicketCode();
            session.AssignTokens(
                publicTokenHash: _tokens.Hash(publicToken),
                publicTokenProtected: _tokens.Protect(publicToken),
                ticketCodeHash: _tokens.Hash(ticketCode),
                ticketCodeProtected: _tokens.Protect(ticketCode));

            await _db.ParkingSessions.AddAsync(session, txct);

            try
            {
                await _db.SaveChangesAsync(txct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                throw new ConflictException("An active session already exists for this plate at this location.");
            }
        }, ct);

        _logger.LogInformation("Entry recorded: session {SessionId} at location {LocationId}", session.Id, location.Id);

        await _realtime.SessionChangedAsync(
            session.TenantId, session.ParkingLocationId,
            new SessionRealtimeEvent(session.Id, session.ParkingLocationId, session.Status.ToString(),
                session.PlateNumberRaw, SessionEventKind.Entered), ct);

        var paymentUrl = _urls.SessionPath(publicToken);
        return new EntryTicketResponse(
            session.Id,
            session.PlateNumberRaw,
            session.VehicleType.ToString(),
            session.EntryTime,
            ticketCode,
            paymentUrl,
            _qr.GeneratePngDataUri(paymentUrl),
            location.Name);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException?.GetType().Name == "PostgresException"
           && (ex.InnerException as dynamic)?.SqlState == "23505";
}

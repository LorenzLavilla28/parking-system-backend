using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Application.Pricing;
using ParkingSaaS.Contracts.Customer;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Services;

namespace ParkingSaaS.Application.Customer;

/// <summary>
/// Public, unauthenticated customer surface. Because there is no tenant in
/// context for these requests, queries deliberately bypass the global tenant
/// filter and scope explicitly by the slug-resolved location or the token hash.
/// Responses are masked and generic so a person who merely knows a plate cannot
/// learn identity, history, or operational detail — only enough to pay.
/// </summary>
public sealed class CustomerPublicService : ICustomerPublicService
{
    private readonly IApplicationDbContext _db;
    private readonly IPlateNormalizer _plateNormalizer;
    private readonly IParkingTokenService _tokens;
    private readonly ILookupThrottle _throttle;
    private readonly ICaptchaVerifier _captcha;
    private readonly ISessionPricingService _pricing;
    private readonly IDateTime _clock;
    private readonly ILogger<CustomerPublicService> _logger;

    public CustomerPublicService(
        IApplicationDbContext db,
        IPlateNormalizer plateNormalizer,
        IParkingTokenService tokens,
        ILookupThrottle throttle,
        ICaptchaVerifier captcha,
        ISessionPricingService pricing,
        IDateTime clock,
        ILogger<CustomerPublicService> logger)
    {
        _db = db;
        _plateNormalizer = plateNormalizer;
        _tokens = tokens;
        _throttle = throttle;
        _captcha = captcha;
        _pricing = pricing;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PublicLocationResponse> GetLocationAsync(string slug, CancellationToken ct)
    {
        var normalizedSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
        var location = await _db.ParkingLocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Slug == normalizedSlug && l.Status == LocationStatus.Active, ct)
            ?? throw new NotFoundException("Parking location not found.");

        return new PublicLocationResponse(location.Slug, location.Name, location.Address);
    }

    public async Task<PlateLookupResponse> LookupByPlateAsync(
        string slug, PlateLookupRequest request, PublicClientContext client, CancellationToken ct)
    {
        var decision = _throttle.Evaluate(client.ClientKey);
        if (decision.IsBlocked)
        {
            _logger.LogWarning("Plate lookup blocked for client {ClientKey} (failures {Failures})",
                client.ClientKey, decision.FailedAttempts);
            throw new ThrottledException();
        }

        // Once enough failures accrue, require a valid CAPTCHA before proceeding.
        if (decision.CaptchaRequired)
        {
            var ok = await _captcha.VerifyAsync(request.CaptchaToken, client.RemoteIp, ct);
            if (!ok)
                return new PlateLookupResponse("captcha_required", null, CaptchaRequired: true);
        }

        var normalizedSlug = (slug ?? string.Empty).Trim().ToLowerInvariant();
        var location = await _db.ParkingLocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Slug == normalizedSlug && l.Status == LocationStatus.Active, ct);

        if (location is null)
        {
            // Do not reveal whether the location exists.
            _throttle.RegisterFailure(client.ClientKey);
            return NotFound(decision);
        }

        var normalizedPlate = _plateNormalizer.Normalize(request.PlateNumber);
        if (string.IsNullOrEmpty(normalizedPlate))
        {
            _throttle.RegisterFailure(client.ClientKey);
            return NotFound(decision);
        }

        // Only ACTIVE sessions at THIS location — never history or other locations.
        var now = _clock.UtcNow;
        var matches = await _db.ParkingSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.ParkingLocationId == location.Id &&
                        s.PlateNumberNormalized == normalizedPlate &&
                        (s.Status == ParkingSessionStatus.ActiveUnpaid ||
                         s.Status == ParkingSessionStatus.PaymentPending ||
                         s.Status == ParkingSessionStatus.PaidExitWindow ||
                         s.Status == ParkingSessionStatus.OverstayDue ||
                         (s.Status == ParkingSessionStatus.PaidExitWindow &&
                          s.PaidExitDeadline != null && s.PaidExitDeadline <= now)))
            .Select(s => new { s.PublicTokenProtected })
            .Take(2)
            .ToListAsync(ct);

        switch (matches.Count)
        {
            case 1:
                _throttle.RegisterSuccess(client.ClientKey);
                var token = _tokens.Unprotect(matches[0].PublicTokenProtected);
                return new PlateLookupResponse("found", token, CaptchaRequired: false);

            case > 1:
                // Ambiguous: ask for the ticket code / guard assistance, reveal nothing.
                return new PlateLookupResponse("multiple", null, CaptchaRequired: false);

            default:
                _logger.LogInformation("Plate lookup miss at location {LocationId}", location.Id);
                _throttle.RegisterFailure(client.ClientKey);
                return NotFound(decision);
        }
    }

    public async Task<PublicSessionResponse> GetSessionByTokenAsync(string publicToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(publicToken))
            throw new NotFoundException("Session not found.");

        var hash = _tokens.Hash(publicToken);
        var session = await _db.ParkingSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.PublicTokenHash == hash, ct)
            ?? throw new NotFoundException("Session not found.");

        var location = await _db.ParkingLocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(l => l.Id == session.ParkingLocationId, ct);

        var now = _clock.UtcNow;
        var effectiveStatus = session.EffectiveStatus(now);

        // Show the live fee only while the session is still open; a closed/exited
        // session reports no current fee.
        decimal? currentFee = null;
        if (effectiveStatus.IsActive())
        {
            var fee = await _pricing.CalculateAsync(session, now, discount: null, ct);
            currentFee = fee?.TotalAmount;
        }

        return new PublicSessionResponse(
            PlateMasker.Mask(session.PlateNumberRaw),
            location.Name,
            session.EntryTime,
            effectiveStatus.ToString(),
            PublicPaymentStatus.From(effectiveStatus),
            currentFee,
            session.PaidExitDeadline);
    }

    private static PlateLookupResponse NotFound(ThrottleDecision decision)
        => new("not_found", null, decision.CaptchaRequired);
}

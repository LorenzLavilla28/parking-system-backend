using Microsoft.EntityFrameworkCore;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Audit;
using ParkingSaaS.Application.Common.Exceptions;
using ParkingSaaS.Contracts.Benefits;
using ParkingSaaS.Domain.Benefits;
using ParkingSaaS.Domain.Locations;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Services;

namespace ParkingSaaS.Application.Benefits;

public sealed class CorporateBenefitService : ICorporateBenefitService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IDateTime _clock;
    private readonly IPlateNormalizer _plateNormalizer;
    private readonly IAuditLogger _audit;

    public CorporateBenefitService(IApplicationDbContext db, ICurrentUser user, IDateTime clock, IPlateNormalizer plateNormalizer, IAuditLogger audit)
    {
        _db = db;
        _user = user;
        _clock = clock;
        _plateNormalizer = plateNormalizer;
        _audit = audit;
    }

    public async Task<IReadOnlyList<CorporateBenefitProgramResponse>> ListAsync(CancellationToken ct)
    {
        var programs = await _db.CorporateBenefitPrograms.AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt).ToListAsync(ct);
        return await MapManyAsync(programs, ct);
    }

    public async Task<CorporateBenefitProgramResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var program = await _db.CorporateBenefitPrograms.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Corporate benefit program not found.");
        return (await MapManyAsync(new[] { program }, ct)).Single();
    }

    public async Task<CorporateBenefitProgramResponse> CreateAsync(CreateCorporateBenefitRequest request, CancellationToken ct)
    {
        var rulesJson = ValidateRules(request.Rules);
        var locations = await ValidateLocationsAsync(request.Locations, ct);
        var plates = NormalizePlates(request.PlateNumbers);
        var now = _clock.UtcNow;
        var effectiveFrom = ValidateEffectiveWindow(request.EffectiveFrom, request.EffectiveTo, now);

        var program = new CorporateBenefitProgram(_user.TenantId, request.Name, request.Description ?? string.Empty, request.Priority);
        program.Activate();
        await _db.CorporateBenefitPrograms.AddAsync(program, ct);

        foreach (var location in locations)
        {
            await _db.CorporateBenefitProgramLocations.AddAsync(
                new CorporateBenefitProgramLocation(_user.TenantId, program.Id, location.Id,
                    request.Locations.Single(x => x.ParkingLocationId == location.Id).MaxConcurrentFreeSessions), ct);
        }

        foreach (var plate in plates)
            await _db.CorporateBenefitPlates.AddAsync(
                new CorporateBenefitPlate(_user.TenantId, program.Id, plate.Normalized, plate.Display, now), ct);

        await _db.CorporateBenefitProgramVersions.AddAsync(
            new CorporateBenefitProgramVersion(_user.TenantId, program.Id, 1, effectiveFrom, rulesJson, _user.UserId ?? Guid.Empty, request.EffectiveTo), ct);
        await _audit.AddAsync(_user.TenantId, locations.First().Id, "CorporateBenefitCreated",
            nameof(CorporateBenefitProgram), program.Id.ToString(), null,
            new { program.Name, program.Priority, Locations = locations.Select(x => x.Id), PlateCount = plates.Count }, null,
            new AuditContext(null, null), ct);
        await _db.SaveChangesAsync(ct);
        return await GetAsync(program.Id, ct);
    }

    public async Task<CorporateBenefitProgramResponse> UpdateAsync(Guid id, UpdateCorporateBenefitRequest request, CancellationToken ct)
    {
        var program = await _db.CorporateBenefitPrograms.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Corporate benefit program not found.");
        if (program.Status == CorporateBenefitProgramStatus.Archived)
            throw new ConflictException("An archived benefit program cannot be edited.");

        var before = new { program.Name, program.Description, program.Priority, Status = program.Status.ToString() };

        var rulesJson = ValidateRules(request.Rules);
        var locations = await ValidateLocationsAsync(request.Locations, ct);
        var plates = NormalizePlates(request.PlateNumbers);
        var now = _clock.UtcNow;
        var effectiveFrom = ValidateEffectiveWindow(request.EffectiveFrom, request.EffectiveTo, now);

        var existingLocations = await _db.CorporateBenefitProgramLocations
            .Where(x => x.CorporateBenefitProgramId == id).ToListAsync(ct);
        var existingPlates = await _db.CorporateBenefitPlates
            .Where(x => x.CorporateBenefitProgramId == id).ToListAsync(ct);
        if (effectiveFrom > now.AddMinutes(1))
        {
            var requestedLocations = request.Locations.ToDictionary(x => x.ParkingLocationId, x => x.MaxConcurrentFreeSessions);
            var currentLocations = existingLocations.ToDictionary(x => x.ParkingLocationId, x => x.MaxConcurrentFreeSessions);
            var currentPlates = existingPlates.Where(x => x.IsActive).Select(x => x.PlateNumberNormalized).ToHashSet(StringComparer.Ordinal);
            var requestedPlates = plates.Select(x => x.Normalized).ToHashSet(StringComparer.Ordinal);
            if (!currentLocations.OrderBy(x => x.Key).SequenceEqual(requestedLocations.OrderBy(x => x.Key)) ||
                !currentPlates.SetEquals(requestedPlates))
                throw new ConflictException("A future revision can change pricing rules only. Apply plate and location changes immediately or schedule them in a separate operational change.");
        }
        // Serialize configuration changes with vehicle entry at every affected
        // location so a capacity reduction cannot race a new allocation.
        foreach (var locationId in existingLocations.Select(x => x.ParkingLocationId)
                     .Concat(request.Locations.Select(x => x.ParkingLocationId))
                     .Distinct().OrderBy(x => x))
            await _db.LockLocationAsync(locationId, ct);
        foreach (var existing in existingLocations)
        {
            var requested = request.Locations.FirstOrDefault(x => x.ParkingLocationId == existing.ParkingLocationId);
            if (requested is null)
            {
                var activeCount = await _db.CorporateBenefitAllocations.CountAsync(a =>
                    a.CorporateBenefitProgramId == id && a.ParkingLocationId == existing.ParkingLocationId &&
                    a.Status == CorporateBenefitAllocationStatus.Active, ct);
                if (activeCount > 0)
                    throw new ConflictException("A location with an active benefit allocation cannot be removed from the program.");
                _db.CorporateBenefitProgramLocations.Remove(existing);
            }
            else
            {
                var activeCount = await _db.CorporateBenefitAllocations.CountAsync(a =>
                    a.CorporateBenefitProgramId == id && a.ParkingLocationId == existing.ParkingLocationId &&
                    a.Status == CorporateBenefitAllocationStatus.Active, ct);
                if (activeCount > requested.MaxConcurrentFreeSessions)
                    throw new ConflictException("The new benefit capacity is below the number of currently allocated sessions.");
                existing.SetCapacity(requested.MaxConcurrentFreeSessions);
            }
        }

        var existingLocationIds = existingLocations.Select(x => x.ParkingLocationId).ToHashSet();
        foreach (var location in locations.Where(x => !existingLocationIds.Contains(x.Id)))
            await _db.CorporateBenefitProgramLocations.AddAsync(
                new CorporateBenefitProgramLocation(_user.TenantId, id, location.Id,
                    request.Locations.Single(x => x.ParkingLocationId == location.Id).MaxConcurrentFreeSessions), ct);

        foreach (var existing in existingPlates)
        {
            var requested = plates.FirstOrDefault(x => x.Normalized == existing.PlateNumberNormalized);
            if (requested != default)
            {
                if (!existing.IsActive) existing.Reactivate(requested.Display, now);
                continue;
            }
            existing.Deactivate(now);
        }

        foreach (var plate in plates.Where(p => existingPlates.All(x => x.PlateNumberNormalized != p.Normalized)))
            await _db.CorporateBenefitPlates.AddAsync(
                new CorporateBenefitPlate(_user.TenantId, id, plate.Normalized, plate.Display, now), ct);

        program.Rename(request.Name);
        program.Describe(request.Description ?? string.Empty);
        program.SetPriority(request.Priority);

        var versions = await _db.CorporateBenefitProgramVersions
            .Where(v => v.CorporateBenefitProgramId == id).ToListAsync(ct);
        if (versions.Any(v => v.EffectiveFrom >= effectiveFrom &&
                              (v.EffectiveTo is null || v.EffectiveTo > effectiveFrom)))
            throw new ConflictException("A future benefit revision is already scheduled. Edit or wait for that revision before adding another change.");
        foreach (var open in versions.Where(v => v.EffectiveFrom < effectiveFrom &&
                                                  (v.EffectiveTo is null || v.EffectiveTo > effectiveFrom)))
            open.Supersede(effectiveFrom);
        await _db.CorporateBenefitProgramVersions.AddAsync(
            new CorporateBenefitProgramVersion(_user.TenantId, id,
                versions.Count == 0 ? 1 : versions.Max(v => v.VersionNumber) + 1,
                effectiveFrom, rulesJson, _user.UserId ?? Guid.Empty, request.EffectiveTo), ct);

        await _audit.AddAsync(_user.TenantId, locations.First().Id, "CorporateBenefitUpdated",
            nameof(CorporateBenefitProgram), program.Id.ToString(), before,
            new { program.Name, program.Description, program.Priority, Status = program.Status.ToString(), Version = versions.Count == 0 ? 1 : versions.Max(v => v.VersionNumber) + 1 }, null,
            new AuditContext(null, null), ct);

        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task SetStatusAsync(Guid id, string status, CancellationToken ct)
    {
        var program = await _db.CorporateBenefitPrograms.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Corporate benefit program not found.");
        if (!Enum.TryParse<CorporateBenefitProgramStatus>(status, true, out var parsed))
            throw new ConflictException("Unknown benefit program status.");
        if (program.Status == CorporateBenefitProgramStatus.Archived && parsed != CorporateBenefitProgramStatus.Archived)
            throw new ConflictException("An archived benefit program cannot be reactivated.");

        var before = program.Status.ToString();
        switch (parsed)
        {
            case CorporateBenefitProgramStatus.Active: program.Activate(); break;
            case CorporateBenefitProgramStatus.Paused: program.Pause(); break;
            case CorporateBenefitProgramStatus.Archived: program.Archive(); break;
            case CorporateBenefitProgramStatus.Draft: program.SetDraft(); break;
        }
        await _audit.AddAsync(_user.TenantId, null, "CorporateBenefitStatusChanged",
            nameof(CorporateBenefitProgram), program.Id.ToString(), new { Status = before },
            new { Status = program.Status.ToString() }, null, new AuditContext(null, null), ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CorporateBenefitAllocationResponse>> ListAllocationsAsync(Guid id, CancellationToken ct)
    {
        if (!await _db.CorporateBenefitPrograms.AnyAsync(p => p.Id == id, ct))
            throw new NotFoundException("Corporate benefit program not found.");
        return await _db.CorporateBenefitAllocations.AsNoTracking()
            .Where(a => a.CorporateBenefitProgramId == id)
            .OrderByDescending(a => a.AllocatedAt)
            .Take(500)
            .Select(a => new CorporateBenefitAllocationResponse(
                a.Id, a.CorporateBenefitProgramId, a.ParkingLocationId, a.ParkingSessionId,
                a.PlateNumberNormalized, a.AllocatedAt, a.ReleasedAt, a.Status.ToString()))
            .ToListAsync(ct);
    }

    public async Task<CorporateBenefitAvailabilityResponse> GetAvailabilityAsync(Guid id, Guid locationId, CancellationToken ct)
    {
        var assignment = await _db.CorporateBenefitProgramLocations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CorporateBenefitProgramId == id && x.ParkingLocationId == locationId, ct)
            ?? throw new NotFoundException("Benefit program location assignment not found.");
        var active = await _db.CorporateBenefitAllocations.CountAsync(a =>
            a.CorporateBenefitProgramId == id && a.ParkingLocationId == locationId &&
            a.Status == CorporateBenefitAllocationStatus.Active, ct);
        return new CorporateBenefitAvailabilityResponse(id, locationId, assignment.MaxConcurrentFreeSessions,
            active, Math.Max(0, assignment.MaxConcurrentFreeSessions - active));
    }

    private async Task<IReadOnlyList<ParkingLocation>> ValidateLocationsAsync(
        IReadOnlyList<CorporateBenefitLocationRequest> requests, CancellationToken ct)
    {
        if (requests is null || requests.Count == 0)
            throw new ConflictException("At least one parking location is required.");
        if (requests.GroupBy(x => x.ParkingLocationId).Any(g => g.Count() > 1))
            throw new ConflictException("A parking location cannot be assigned more than once.");
        if (requests.Any(x => x.MaxConcurrentFreeSessions is < 1 or > 100000))
            throw new ConflictException("Benefit capacity must be between 1 and 100,000.");

        var ids = requests.Select(x => x.ParkingLocationId).ToArray();
        var locations = await _db.ParkingLocations.Where(l => ids.Contains(l.Id) && l.Status == LocationStatus.Active).ToListAsync(ct);
        if (locations.Count != ids.Length)
            throw new ConflictException("All assigned parking locations must be active and belong to this tenant.");
        return locations;
    }

    private List<(string Normalized, string Display)> NormalizePlates(IReadOnlyList<string> values)
    {
        if (values is null || values.Count == 0)
            throw new ConflictException("At least one eligible plate number is required.");
        var result = new List<(string Normalized, string Display)>();
        foreach (var value in values)
        {
            var normalized = _plateNormalizer.Normalize(value);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new ConflictException("Plate numbers cannot be empty after normalization.");
            if (result.Any(x => x.Normalized == normalized)) continue;
            result.Add((normalized, value.Trim()));
        }
        if (result.Count > 10000)
            throw new ConflictException("A benefit program cannot contain more than 10,000 plates.");
        return result;
    }

    private static string ValidateRules(CorporateBenefitRulesRequest request)
    {
        var rules = new CorporateBenefitRules
        {
            Windows = request.Windows?.Select(x => new CorporateBenefitTimeWindow { Start = x.Start, End = x.End }).ToList() ?? new(),
            DaysOfWeek = request.DaysOfWeek?.ToList() ?? new(),
            Holidays = request.Holidays?.ToList() ?? new(),
            ExcludeHolidays = request.ExcludeHolidays,
            EligibleVehicleTypes = request.EligibleVehicleTypes?.ToList() ?? new(),
            OutsideWindowBehavior = request.OutsideWindowBehavior
        };
        var errors = rules.Validate();
        if (errors.Count > 0) throw new ConflictException($"Invalid benefit rules: {string.Join("; ", errors)}");
        return rules.Serialize();
    }

    private async Task<IReadOnlyList<CorporateBenefitProgramResponse>> MapManyAsync(
        IReadOnlyList<CorporateBenefitProgram> programs, CancellationToken ct)
    {
        var ids = programs.Select(p => p.Id).ToArray();
        var assignments = await _db.CorporateBenefitProgramLocations.AsNoTracking().Where(x => ids.Contains(x.CorporateBenefitProgramId)).ToListAsync(ct);
        var locationIds = assignments.Select(x => x.ParkingLocationId).Distinct().ToArray();
        var locations = await _db.ParkingLocations.AsNoTracking().Where(x => locationIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var now = _clock.UtcNow;
        var versions = await _db.CorporateBenefitProgramVersions.AsNoTracking()
            .Where(x => ids.Contains(x.CorporateBenefitProgramId) && x.EffectiveFrom <= now &&
                        (x.EffectiveTo == null || x.EffectiveTo > now)).ToListAsync(ct);
        var plates = await _db.CorporateBenefitPlates.AsNoTracking().Where(x => ids.Contains(x.CorporateBenefitProgramId)).OrderBy(x => x.PlateNumberNormalized).ToListAsync(ct);
        var allocationCounts = await _db.CorporateBenefitAllocations.AsNoTracking().Where(x => ids.Contains(x.CorporateBenefitProgramId) && x.Status == CorporateBenefitAllocationStatus.Active).GroupBy(x => new { x.CorporateBenefitProgramId, x.ParkingLocationId }).Select(g => new { g.Key.CorporateBenefitProgramId, g.Key.ParkingLocationId, Count = g.Count() }).ToListAsync(ct);

        return programs.Select(program =>
        {
            var version = versions.Where(x => x.CorporateBenefitProgramId == program.Id).OrderByDescending(x => x.VersionNumber).FirstOrDefault();
            var rules = version is null ? new CorporateBenefitRules() : CorporateBenefitRules.Parse(version.RulesJson);
            var programAssignments = assignments.Where(x => x.CorporateBenefitProgramId == program.Id).Select(x =>
            {
                var count = allocationCounts.FirstOrDefault(c => c.CorporateBenefitProgramId == program.Id && c.ParkingLocationId == x.ParkingLocationId)?.Count ?? 0;
                return new CorporateBenefitLocationResponse(x.ParkingLocationId, locations[x.ParkingLocationId].Name, x.MaxConcurrentFreeSessions, count);
            }).ToArray();
            return new CorporateBenefitProgramResponse(
                program.Id, program.Name, program.Description, program.Priority, program.Status.ToString(),
                version?.VersionNumber ?? 0, programAssignments, ToRequest(rules),
                plates.Where(x => x.CorporateBenefitProgramId == program.Id).Select(x => new CorporateBenefitPlateResponse(x.Id, x.PlateNumberDisplay, x.IsActive, x.EffectiveFrom, x.EffectiveTo)).ToArray(),
                program.CreatedAt, program.UpdatedAt, version?.EffectiveFrom, version?.EffectiveTo);
        }).ToArray();
    }

    private static CorporateBenefitRulesRequest ToRequest(CorporateBenefitRules rules)
        => new(rules.Windows.Select(x => new CorporateBenefitWindowRequest(x.Start, x.End)).ToArray(), rules.DaysOfWeek,
            rules.Holidays, rules.ExcludeHolidays, rules.EligibleVehicleTypes, rules.OutsideWindowBehavior);

    private static DateTimeOffset ValidateEffectiveWindow(DateTimeOffset? requestedFrom, DateTimeOffset? requestedTo, DateTimeOffset now)
    {
        var from = requestedFrom ?? now;
        if (from < now.AddMinutes(-1))
            throw new ConflictException("A benefit revision cannot be scheduled in the past.");
        if (requestedTo is { } to && to <= from)
            throw new ConflictException("Benefit effective end must be after its effective start.");
        return from;
    }
}

public sealed class CorporateBenefitAllocationService : ICorporateBenefitAllocationService
{
    private readonly IApplicationDbContext _db;

    public CorporateBenefitAllocationService(IApplicationDbContext db) => _db = db;

    public async Task<BenefitAllocationDecision> TryAllocateAsync(
        Guid tenantId, Guid parkingLocationId, Guid parkingSessionId, string normalizedPlate,
        VehicleType vehicleType, DateTimeOffset at, CancellationToken ct)
    {
        var location = await _db.ParkingLocations.AsNoTracking().FirstAsync(x => x.Id == parkingLocationId, ct);
        var candidatePlates = await _db.CorporateBenefitPlates
            .Where(x => x.PlateNumberNormalized == normalizedPlate && x.IsActive && x.EffectiveFrom <= at &&
                        (x.EffectiveTo == null || x.EffectiveTo > at))
            .ToListAsync(ct);
        if (candidatePlates.Count == 0)
            return new BenefitAllocationDecision(false, null, null, null);

        var candidates = await _db.CorporateBenefitPrograms
            .Where(x => x.Status == CorporateBenefitProgramStatus.Active && candidatePlates.Select(p => p.CorporateBenefitProgramId).Contains(x.Id))
            .OrderByDescending(x => x.Priority).ThenBy(x => x.CreatedAt).ToListAsync(ct);
        string? unavailableProgram = null;

        foreach (var program in candidates)
        {
            var assignment = await _db.CorporateBenefitProgramLocations.FirstOrDefaultAsync(x =>
                x.CorporateBenefitProgramId == program.Id && x.ParkingLocationId == parkingLocationId, ct);
            if (assignment is null) continue;

            var version = await _db.CorporateBenefitProgramVersions
                .Where(x => x.CorporateBenefitProgramId == program.Id && x.EffectiveFrom <= at &&
                            (x.EffectiveTo == null || x.EffectiveTo > at))
                .OrderByDescending(x => x.VersionNumber).FirstOrDefaultAsync(ct);
            if (version is null) continue;

            var rules = CorporateBenefitRules.Parse(version.RulesJson);
            if (!rules.IsVehicleEligible(vehicleType)) continue;
            // Reserve the shared entitlement when the session overlaps a
            // configured window in the next scheduling cycle. This covers a
            // late-week entry before the next configured weekday without
            // reserving arbitrary plates forever.
            if (rules.GetFreeIntervals(at, at.AddDays(7), location.Timezone).Count == 0) continue;

            var activeCount = await _db.CorporateBenefitAllocations.CountAsync(x =>
                x.CorporateBenefitProgramId == program.Id && x.ParkingLocationId == parkingLocationId &&
                x.Status == CorporateBenefitAllocationStatus.Active, ct);
            if (activeCount >= assignment.MaxConcurrentFreeSessions)
            {
                unavailableProgram ??= program.Name;
                continue;
            }

            var allocation = new CorporateBenefitAllocation(
                tenantId, program.Id, version.Id, parkingLocationId, parkingSessionId, normalizedPlate, at);
            await _db.CorporateBenefitAllocations.AddAsync(allocation, ct);
            return new BenefitAllocationDecision(true, program.Name, "Corporate benefit allocated.", allocation.Id);
        }

        return unavailableProgram is null
            ? new BenefitAllocationDecision(false, null, null, null)
            : new BenefitAllocationDecision(false, unavailableProgram, "Eligible plate, but the shared benefit capacity is occupied.", null);
    }

    public async Task ReleaseForSessionAsync(Guid sessionId, DateTimeOffset at, CancellationToken ct)
    {
        var allocation = await _db.CorporateBenefitAllocations.FirstOrDefaultAsync(x => x.ParkingSessionId == sessionId, ct);
        allocation?.Release(at);
    }
}

using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Benefits;

public enum CorporateBenefitProgramStatus
{
    Draft = 1,
    Active = 2,
    Paused = 3,
    Archived = 4
}

/// <summary>
/// A tenant-owned corporate parking benefit. The program identifies the
/// business; immutable versions hold pricing rules and allocations reserve its
/// shared concurrent capacity.
/// </summary>
public sealed class CorporateBenefitProgram : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public int Priority { get; private set; }
    public CorporateBenefitProgramStatus Status { get; private set; } = CorporateBenefitProgramStatus.Draft;

    private CorporateBenefitProgram() { }

    public CorporateBenefitProgram(Guid tenantId, string name, string description, int priority)
    {
        if (tenantId == Guid.Empty) throw new DomainException("benefit.tenant_required", "Tenant is required.");
        TenantId = tenantId;
        Rename(name);
        Describe(description);
        SetPriority(priority);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("benefit.name_required", "Benefit program name is required.");
        Name = name.Trim();
    }

    public void Describe(string description) => Description = description?.Trim() ?? string.Empty;

    public void SetPriority(int priority)
    {
        if (priority is < 0 or > 10000)
            throw new DomainException("benefit.priority_invalid", "Benefit priority must be between 0 and 10,000.");
        Priority = priority;
    }

    public void Activate() => Status = CorporateBenefitProgramStatus.Active;
    public void Pause() => Status = CorporateBenefitProgramStatus.Paused;
    public void Archive() => Status = CorporateBenefitProgramStatus.Archived;
    public void SetDraft() => Status = CorporateBenefitProgramStatus.Draft;

    public bool IsActive => Status == CorporateBenefitProgramStatus.Active;
}

/// <summary>Tenant-owned location assignment and capacity for a benefit program.</summary>
public sealed class CorporateBenefitProgramLocation : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid CorporateBenefitProgramId { get; private set; }
    public Guid ParkingLocationId { get; private set; }
    public int MaxConcurrentFreeSessions { get; private set; }

    private CorporateBenefitProgramLocation() { }

    public CorporateBenefitProgramLocation(Guid tenantId, Guid programId, Guid parkingLocationId, int maxConcurrentFreeSessions)
    {
        if (tenantId == Guid.Empty) throw new DomainException("benefit.tenant_required", "Tenant is required.");
        if (programId == Guid.Empty) throw new DomainException("benefit.program_required", "Benefit program is required.");
        if (parkingLocationId == Guid.Empty) throw new DomainException("benefit.location_required", "Parking location is required.");
        TenantId = tenantId;
        CorporateBenefitProgramId = programId;
        ParkingLocationId = parkingLocationId;
        SetCapacity(maxConcurrentFreeSessions);
    }

    public void SetCapacity(int capacity)
    {
        if (capacity is < 1 or > 100000)
            throw new DomainException("benefit.capacity_invalid", "Benefit capacity must be between 1 and 100,000.");
        MaxConcurrentFreeSessions = capacity;
    }
}

/// <summary>Immutable configuration revision for a corporate benefit program.</summary>
public sealed class CorporateBenefitProgramVersion : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid CorporateBenefitProgramId { get; private set; }
    public int VersionNumber { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }
    public string RulesJson { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CorporateBenefitProgramVersion() { }

    public CorporateBenefitProgramVersion(
        Guid tenantId, Guid programId, int versionNumber, DateTimeOffset effectiveFrom,
        string rulesJson, Guid createdByUserId, DateTimeOffset? effectiveTo = null)
    {
        TenantId = tenantId;
        CorporateBenefitProgramId = programId;
        VersionNumber = versionNumber;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        RulesJson = rulesJson;
        CreatedByUserId = createdByUserId;
        CreatedAt = effectiveFrom;
    }

    public void Supersede(DateTimeOffset at) => EffectiveTo = at;

    public bool IsEffectiveAt(DateTimeOffset at)
        => EffectiveFrom <= at && (EffectiveTo is null || EffectiveTo > at);
}

/// <summary>Normalized plate membership in a tenant's benefit program.</summary>
public sealed class CorporateBenefitPlate : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid CorporateBenefitProgramId { get; private set; }
    public string PlateNumberNormalized { get; private set; } = string.Empty;
    public string PlateNumberDisplay { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }

    private CorporateBenefitPlate() { }

    public CorporateBenefitPlate(
        Guid tenantId, Guid programId, string normalizedPlate, string displayPlate, DateTimeOffset effectiveFrom)
    {
        TenantId = tenantId;
        CorporateBenefitProgramId = programId;
        PlateNumberNormalized = normalizedPlate;
        PlateNumberDisplay = displayPlate.Trim();
        EffectiveFrom = effectiveFrom;
    }

    public void Deactivate(DateTimeOffset at)
    {
        IsActive = false;
        EffectiveTo ??= at;
    }

    public void Reactivate(string displayPlate, DateTimeOffset at)
    {
        PlateNumberDisplay = displayPlate.Trim();
        IsActive = true;
        EffectiveFrom = EffectiveFrom > at ? EffectiveFrom : at;
        EffectiveTo = null;
    }

    public bool MatchesAt(DateTimeOffset at)
        => IsActive && EffectiveFrom <= at && (EffectiveTo is null || EffectiveTo > at);
}

public enum CorporateBenefitAllocationStatus
{
    Active = 1,
    Released = 2
}

/// <summary>Runtime reservation of one shared benefit slot by a parking session.</summary>
public sealed class CorporateBenefitAllocation : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid CorporateBenefitProgramId { get; private set; }
    public Guid CorporateBenefitProgramVersionId { get; private set; }
    public Guid ParkingLocationId { get; private set; }
    public Guid ParkingSessionId { get; private set; }
    public string PlateNumberNormalized { get; private set; } = string.Empty;
    public DateTimeOffset AllocatedAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }
    public CorporateBenefitAllocationStatus Status { get; private set; } = CorporateBenefitAllocationStatus.Active;

    private CorporateBenefitAllocation() { }

    public CorporateBenefitAllocation(
        Guid tenantId, Guid programId, Guid versionId, Guid parkingLocationId, Guid parkingSessionId,
        string plateNumberNormalized, DateTimeOffset allocatedAt)
    {
        TenantId = tenantId;
        CorporateBenefitProgramId = programId;
        CorporateBenefitProgramVersionId = versionId;
        ParkingLocationId = parkingLocationId;
        ParkingSessionId = parkingSessionId;
        PlateNumberNormalized = plateNumberNormalized;
        AllocatedAt = allocatedAt;
    }

    public void Release(DateTimeOffset at)
    {
        if (Status == CorporateBenefitAllocationStatus.Released) return;
        Status = CorporateBenefitAllocationStatus.Released;
        ReleasedAt = at;
    }
}

using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.RatePlans;

public enum RatePlanStatus
{
    Draft = 1,
    Active = 2,
    Archived = 3
}

/// <summary>
/// A reusable named pricing configuration. Editing prices never
/// mutates an existing version; a new <see cref="RatePlanVersion"/> is appended,
/// so sessions priced under an earlier version keep their original terms.
/// </summary>
public class RatePlan : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid? ParkingLocationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public RatePlanStatus Status { get; private set; } = RatePlanStatus.Draft;

    private RatePlan() { }

    public RatePlan(Guid tenantId, string name, string description, Guid? parkingLocationId = null)
    {
        if (tenantId == Guid.Empty) throw new DomainException("rateplan.tenant_required", "Tenant is required.");
        TenantId = tenantId;
        ParkingLocationId = parkingLocationId;
        Rename(name);
        Describe(description);
        Status = RatePlanStatus.Draft;
    }

    // Compatibility constructor for existing domain callers; new plans should
    // use the reusable-template constructor above.
    public RatePlan(Guid tenantId, Guid parkingLocationId, string name)
        : this(tenantId, name, "Legacy rate plan", parkingLocationId)
    {
        if (parkingLocationId == Guid.Empty) throw new DomainException("rateplan.location_required", "Location is required.");
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("rateplan.name_required", "Rate plan name is required.");
        Name = name.Trim();
    }

    public void Describe(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("rateplan.description_required", "Rate plan description is required.");
        Description = description.Trim();
    }

    public void Activate() => Status = RatePlanStatus.Active;
    public void Archive() => Status = RatePlanStatus.Archived;
    public bool IsActive => Status == RatePlanStatus.Active;
}

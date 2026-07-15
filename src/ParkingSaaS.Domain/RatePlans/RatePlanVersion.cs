using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.RatePlans;

/// <summary>
/// An immutable snapshot of a rate plan's rules, effective over a time range.
/// A session pins the version that applied at entry; the rules JSON is never
/// edited in place.
/// </summary>
public class RatePlanVersion : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid RatePlanId { get; private set; }
    public int VersionNumber { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }
    public string RulesJson { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private RatePlanVersion() { }

    public RatePlanVersion(
        Guid tenantId, Guid ratePlanId, int versionNumber,
        DateTimeOffset effectiveFrom, string rulesJson, Guid createdByUserId)
    {
        TenantId = tenantId;
        RatePlanId = ratePlanId;
        VersionNumber = versionNumber;
        EffectiveFrom = effectiveFrom;
        RulesJson = rulesJson;
        CreatedByUserId = createdByUserId;
        CreatedAt = effectiveFrom;
    }

    /// <summary>Closes this version's effective window when a newer version supersedes it.</summary>
    public void Supersede(DateTimeOffset at) => EffectiveTo = at;

    public bool IsEffectiveAt(DateTimeOffset at)
        => EffectiveFrom <= at && (EffectiveTo is null || EffectiveTo > at);
}

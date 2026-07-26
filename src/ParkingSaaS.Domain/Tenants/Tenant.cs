using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Tenants;

/// <summary>
/// A parking operator. The aggregate root that owns every other tenant-scoped
/// record in the system. Platform administrators are the only actors allowed
/// to create or change a tenant's lifecycle status.
/// </summary>
public class Tenant : AuditableEntity
{
    public string Name { get; private set; } = string.Empty;

    /// <summary>URL-safe identifier used in public location/tenant routing.</summary>
    public string Slug { get; private set; } = string.Empty;

    public TenantStatus Status { get; private set; } = TenantStatus.Active;
    public SubscriptionPlan SubscriptionPlan { get; private set; } = SubscriptionPlan.Free;

    /// <summary>Additional slots granted per location outside the base plan allowance.</summary>
    public int AdditionalSlotCapacity { get; private set; }

    /// <summary>ISO 4217 code, e.g. "PHP". Used as the default for new locations.</summary>
    public string DefaultCurrency { get; private set; } = "PHP";

    /// <summary>IANA timezone, e.g. "Asia/Manila".</summary>
    public string DefaultTimezone { get; private set; } = "Asia/Manila";

    private Tenant() { }

    public Tenant(string name, string slug, SubscriptionPlan plan, string currency, string timezone)
    {
        Rename(name);
        SetSlug(slug);
        SubscriptionPlan = plan;
        DefaultCurrency = string.IsNullOrWhiteSpace(currency) ? "PHP" : currency.Trim().ToUpperInvariant();
        DefaultTimezone = string.IsNullOrWhiteSpace(timezone) ? "Asia/Manila" : timezone.Trim();
        Status = TenantStatus.Active;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("tenant.name_required", "Tenant name is required.");
        Name = name.Trim();
    }

    public void SetSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new DomainException("tenant.slug_required", "Tenant slug is required.");
        Slug = slug.Trim().ToLowerInvariant();
    }

    public void ChangeStatus(TenantStatus status) => Status = status;

    public void ChangePlan(SubscriptionPlan plan) => SubscriptionPlan = plan;

    public void SetAdditionalSlotCapacity(int capacity)
    {
        if (capacity is < 0 or > 100000)
            throw new DomainException("tenant.additional_capacity_invalid", "Additional capacity must be between 0 and 100,000 slots.");
        AdditionalSlotCapacity = capacity;
    }

    public bool IsActive => Status == TenantStatus.Active;
}

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

    /// <summary>Selected/billed capacity per location before any additional capacity add-on.</summary>
    public int? PurchasedSlotCapacityPerLocation { get; private set; }

    /// <summary>
    /// True for tenants created under capacity-based pricing. Existing tenants are
    /// migrated with this disabled so their current fixed plan price is preserved
    /// until a capacity change is explicitly made.
    /// </summary>
    public bool CapacityPricingEnabled { get; private set; }

    /// <summary>Additional slots granted per location above the selected capacity.</summary>
    public int AdditionalSlotCapacity { get; private set; }

    /// <summary>ISO 4217 code, e.g. "PHP". Used as the default for new locations.</summary>
    public string DefaultCurrency { get; private set; } = "PHP";

    /// <summary>IANA timezone, e.g. "Asia/Manila".</summary>
    public string DefaultTimezone { get; private set; } = "Asia/Manila";

    /// <summary>Private object-storage key for the optional tenant logo.</summary>
    public string? LogoObjectKey { get; private set; }

    public string? LogoContentType { get; private set; }

    private Tenant() { }

    public Tenant(string name, string slug, SubscriptionPlan plan, string currency, string timezone)
    {
        Rename(name);
        SetSlug(slug);
        SubscriptionPlan = plan;
        DefaultCurrency = string.IsNullOrWhiteSpace(currency) ? "PHP" : currency.Trim().ToUpperInvariant();
        DefaultTimezone = string.IsNullOrWhiteSpace(timezone) ? "Asia/Manila" : timezone.Trim();
        Status = TenantStatus.Active;
        PurchasedSlotCapacityPerLocation = SubscriptionPlanRules.For(plan).MaximumSlotsPerLocation;
        CapacityPricingEnabled = plan != SubscriptionPlan.Custom;
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

    public void SetPurchasedSlotCapacityPerLocation(int? capacity)
    {
        if (capacity is < 1 or > 100000)
            throw new DomainException("tenant.purchased_capacity_invalid", "Purchased capacity must be between 1 and 100,000 slots.");
        PurchasedSlotCapacityPerLocation = capacity;
    }

    public void SetCapacityPricingEnabled(bool enabled) => CapacityPricingEnabled = enabled;

    public void SetAdditionalSlotCapacity(int capacity)
    {
        if (capacity is < 0 or > 100000)
            throw new DomainException("tenant.additional_capacity_invalid", "Additional capacity must be between 0 and 100,000 slots.");
        AdditionalSlotCapacity = capacity;
    }

    public void SetLogo(string objectKey, string contentType)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
            throw new DomainException("tenant.logo_key_required", "A logo object key is required.");
        if (string.IsNullOrWhiteSpace(contentType))
            throw new DomainException("tenant.logo_content_type_required", "A logo content type is required.");

        LogoObjectKey = objectKey.Trim();
        LogoContentType = contentType.Trim().ToLowerInvariant();
    }

    public void ClearLogo()
    {
        LogoObjectKey = null;
        LogoContentType = null;
    }

    public bool IsActive => Status == TenantStatus.Active;
}

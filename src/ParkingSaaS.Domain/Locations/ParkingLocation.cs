using ParkingSaaS.Domain.Common;

namespace ParkingSaaS.Domain.Locations;

public enum LocationStatus
{
    Active = 1,
    Inactive = 2,
    Archived = 3
}

/// <summary>
/// A physical parking facility owned by a tenant. Guards and supervisors are
/// assigned to specific locations; customers reach a location's public page via
/// its <see cref="Slug"/>.
/// </summary>
public class ParkingLocation : AuditableEntity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public string Timezone { get; private set; } = "Asia/Manila";
    public LocationStatus Status { get; private set; } = LocationStatus.Active;

    /// <summary>Minutes a customer has to leave after paying before overstay applies.</summary>
    public int ExitGraceMinutes { get; private set; } = 15;

    public bool AllowCashPayment { get; private set; } = true;
    public Guid? ActiveRatePlanId { get; private set; }

    /// <summary>The permanent lost-ticket recovery URL for this location.</summary>
    public string? PublicQrCodeUrl { get; private set; }

    private ParkingLocation() { }

    public ParkingLocation(Guid tenantId, string name, string slug, string timezone, string? address)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException("location.tenant_required", "A location must belong to a tenant.");
        TenantId = tenantId;
        Rename(name);
        SetSlug(slug);
        Timezone = string.IsNullOrWhiteSpace(timezone) ? "Asia/Manila" : timezone.Trim();
        Address = address?.Trim();
        Status = LocationStatus.Active;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("location.name_required", "Location name is required.");
        Name = name.Trim();
    }

    public void SetSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new DomainException("location.slug_required", "Location slug is required.");
        Slug = slug.Trim().ToLowerInvariant();
    }

    public void UpdateDetails(string? address, string timezone, int exitGraceMinutes, bool allowCashPayment)
    {
        Address = address?.Trim();
        if (!string.IsNullOrWhiteSpace(timezone))
            Timezone = timezone.Trim();
        if (exitGraceMinutes < 0)
            throw new DomainException("location.grace_invalid", "Exit grace minutes cannot be negative.");
        ExitGraceMinutes = exitGraceMinutes;
        AllowCashPayment = allowCashPayment;
    }

    public void SetPublicQrCodeUrl(string url) => PublicQrCodeUrl = url;

    public void AssignRatePlan(Guid? ratePlanId) => ActiveRatePlanId = ratePlanId;

    public void ChangeStatus(LocationStatus status) => Status = status;

    public bool IsActive => Status == LocationStatus.Active;
}

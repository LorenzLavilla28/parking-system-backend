namespace ParkingSaaS.Domain.Tenants;

public enum TenantStatus
{
    Active = 1,
    Suspended = 2,
    Archived = 3
}

public enum SubscriptionPlan
{
    /// <summary>Legacy value retained for existing records; unavailable for new onboarding.</summary>
    Free = 1,
    Starter = 2,
    Growth = 3,
    Enterprise = 4,
    Custom = 5
}

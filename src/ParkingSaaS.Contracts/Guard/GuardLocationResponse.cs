namespace ParkingSaaS.Contracts.Guard;

/// <summary>A location a guard/supervisor may operate, for the guard UI.</summary>
public sealed record GuardLocationResponse(
    Guid Id,
    string Name,
    string Slug,
    string Timezone,
    bool AllowCashPayment);

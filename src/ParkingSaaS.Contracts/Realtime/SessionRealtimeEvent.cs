namespace ParkingSaaS.Contracts.Realtime;

/// <summary>
/// A lightweight "something about this session changed" signal pushed over SignalR.
/// It carries only enough for a client to decide what to refetch and (optionally)
/// notify on — the server remains authoritative, so clients re-read via the API
/// rather than trusting these fields for business data.
/// </summary>
public sealed record SessionRealtimeEvent(
    Guid SessionId,
    Guid ParkingLocationId,
    string Status,
    string PlateNumberRaw,
    string Kind);

/// <summary>Well-known values for <see cref="SessionRealtimeEvent.Kind"/>.</summary>
public static class SessionEventKind
{
    public const string Entered = "Entered";
    public const string Exited = "Exited";
    public const string PaymentRecorded = "PaymentRecorded";
    public const string PaymentAbandoned = "PaymentAbandoned";
    public const string OverstayDue = "OverstayDue";
    public const string Overridden = "Overridden";
}

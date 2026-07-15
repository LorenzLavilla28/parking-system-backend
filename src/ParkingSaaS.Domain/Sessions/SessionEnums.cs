namespace ParkingSaaS.Domain.Sessions;

/// <summary>
/// Built-in vehicle categories. Tenant-defined types may be layered on later;
/// this enum is the baseline used for rate selection.
/// </summary>
public enum VehicleType
{
    Car = 1,
    Motorcycle = 2,
    Van = 3,
    Truck = 4,
    Other = 99
}

/// <summary>
/// Lifecycle of a parking session. See the state machine in the architecture
/// document. Terminal states: <see cref="Exited"/>, <see cref="Void"/>,
/// <see cref="Cancelled"/>.
/// </summary>
public enum ParkingSessionStatus
{
    ActiveUnpaid = 1,
    PaymentPending = 2,
    PaidExitWindow = 3,
    OverstayDue = 4,
    Exited = 5,
    Void = 6,
    Cancelled = 7
}

public static class ParkingSessionStatusExtensions
{
    /// <summary>States in which a vehicle is still considered parked at the location.</summary>
    public static readonly ParkingSessionStatus[] ActiveStates =
    {
        ParkingSessionStatus.ActiveUnpaid,
        ParkingSessionStatus.PaymentPending,
        ParkingSessionStatus.PaidExitWindow,
        ParkingSessionStatus.OverstayDue
    };

    public static bool IsActive(this ParkingSessionStatus status)
        => Array.IndexOf(ActiveStates, status) >= 0;
}

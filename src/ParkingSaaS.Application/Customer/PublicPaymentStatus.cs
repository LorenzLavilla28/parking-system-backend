using ParkingSaaS.Domain.Sessions;

namespace ParkingSaaS.Application.Customer;

/// <summary>
/// Maps internal session status to a coarse, customer-facing payment label.
/// Deliberately coarse so the public page reveals nothing operational.
/// </summary>
internal static class PublicPaymentStatus
{
    public static string From(ParkingSessionStatus status) => status switch
    {
        ParkingSessionStatus.ActiveUnpaid => "Unpaid",
        ParkingSessionStatus.PaymentPending => "Processing",
        ParkingSessionStatus.PaidExitWindow => "Paid",
        ParkingSessionStatus.OverstayDue => "AdditionalPaymentRequired",
        ParkingSessionStatus.Exited => "Closed",
        _ => "Unavailable"
    };
}

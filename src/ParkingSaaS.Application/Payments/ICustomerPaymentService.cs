using ParkingSaaS.Contracts.Customer;

namespace ParkingSaaS.Application.Payments;

public interface ICustomerPaymentService
{
    /// <summary>Creates (or reuses) a PayMongo checkout for a fee quote and returns the hosted URL.</summary>
    Task<CheckoutResponse> CreateCheckoutAsync(StartCheckoutRequest request, CancellationToken ct);

    Task<CheckoutResponse> CreateCheckoutAsync(
        StartCheckoutRequest request, string? ipAddress, string? deviceInformation, CancellationToken ct);

    /// <summary>Creates (or reuses) a PayMongo dynamic QR Ph payment for a fee quote.</summary>
    Task<CheckoutResponse> CreateDynamicQrAsync(
        StartCheckoutRequest request, string? ipAddress, string? deviceInformation, CancellationToken ct);

    /// <summary>Recovers the latest open dynamic QR payment for a public parking session, if one exists.</summary>
    Task<CheckoutResponse?> GetActivePaymentAsync(string publicToken, CancellationToken ct);

    /// <summary>Returns the current payment status for the public reference (status-page polling).</summary>
    Task<PaymentStatusResponse> GetStatusAsync(string paymentReference, CancellationToken ct);
}

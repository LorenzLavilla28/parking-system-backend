using ParkingSaaS.Contracts.Customer;

namespace ParkingSaaS.Application.Customer;

public interface ICustomerPricingService
{
    /// <summary>Recomputes and returns the current fee for the session behind a public token.</summary>
    Task<CurrentFeeResponse> GetCurrentFeeAsync(string publicToken, CancellationToken ct);

    /// <summary>Recalculates and persists a short-lived, immutable fee quote to pay against.</summary>
    Task<FeeQuoteResponse> CreateQuoteAsync(CreateFeeQuoteRequest request, CancellationToken ct);
}

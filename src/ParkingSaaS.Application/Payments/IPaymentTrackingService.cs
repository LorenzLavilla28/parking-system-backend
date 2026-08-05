using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Payments;

namespace ParkingSaaS.Application.Payments;

public interface IPaymentTrackingService
{
    Task<PagedResult<PaymentSummaryResponse>> SearchAsync(PaymentQueryRequest request, CancellationToken ct);
    Task<IReadOnlyList<PaymentOverrideResponse>> ListOverridesAsync(PaymentOverrideQueryRequest request, CancellationToken ct);
    Task<PaymentDetailResponse> GetAsync(Guid id, CancellationToken ct);
    Task<byte[]> ExportCsvAsync(PaymentQueryRequest request, CancellationToken ct);
}

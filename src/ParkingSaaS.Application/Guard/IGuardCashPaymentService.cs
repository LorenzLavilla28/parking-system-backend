using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Application.Guard;

public interface IGuardCashPaymentService
{
    Task<CashReceiptResponse> RecordCashPaymentAsync(RecordCashPaymentRequest request, string? ipAddress, CancellationToken ct);
}

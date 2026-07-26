using ParkingSaaS.Contracts.Payments;

namespace ParkingSaaS.Application.Payments;

public interface IPayMongoConnectionService
{
    Task<IReadOnlyList<PayMongoConnectionResponse>> GetAsync(CancellationToken cancellationToken);
    Task<PayMongoConnectionResponse> ConnectAsync(ConnectPayMongoRequest request, CancellationToken cancellationToken);
    Task<PayMongoConnectionResponse> DisconnectAsync(string environment, CancellationToken cancellationToken);
}

using ParkingSaaS.Contracts.Platform;

namespace ParkingSaaS.Application.Platform;

public interface IPlatformAdministratorService
{
    Task<IReadOnlyList<PlatformAdministratorResponse>> ListAsync(CancellationToken ct);
    Task<InvitePlatformAdministratorResponse> InviteAsync(InvitePlatformAdministratorRequest request, CancellationToken ct);
}

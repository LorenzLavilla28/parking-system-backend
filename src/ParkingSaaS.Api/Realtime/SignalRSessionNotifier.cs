using Microsoft.AspNetCore.SignalR;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Contracts.Realtime;

namespace ParkingSaaS.Api.Realtime;

/// <summary>
/// SignalR implementation of <see cref="ISessionRealtimeNotifier"/>. Sends the
/// <c>SessionChanged</c> client event to both the affected location group and the
/// tenant group. Failures are swallowed and logged: a realtime hiccup must never
/// fail the business transaction that produced the change.
/// </summary>
public sealed class SignalRSessionNotifier : ISessionRealtimeNotifier
{
    private const string ClientMethod = "SessionChanged";

    private readonly IHubContext<SessionsHub> _hub;
    private readonly ILogger<SignalRSessionNotifier> _logger;

    public SignalRSessionNotifier(IHubContext<SessionsHub> hub, ILogger<SignalRSessionNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task SessionChangedAsync(
        Guid tenantId, Guid parkingLocationId, SessionRealtimeEvent evt, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients
                .Groups(SessionGroups.Location(parkingLocationId), SessionGroups.Tenant(tenantId))
                .SendAsync(ClientMethod, evt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast session change for {SessionId}.", evt.SessionId);
        }
    }
}

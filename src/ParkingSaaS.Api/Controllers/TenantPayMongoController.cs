using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Payments;

namespace ParkingSaaS.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
[Route("api/tenant/payments/paymongo")]
public sealed class TenantPayMongoController : ApiControllerBase
{
    private readonly IPayMongoConnectionService _connections;

    public TenantPayMongoController(IPayMongoConnectionService connections)
        => _connections = connections;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<PayMongoConnectionResponse>>.Ok(await _connections.GetAsync(ct)));

    [HttpPost("connect")]
    public async Task<IActionResult> Connect([FromBody] ConnectPayMongoRequest request, CancellationToken ct)
        => Ok(ApiResponse<PayMongoConnectionResponse>.Ok(await _connections.ConnectAsync(request, ct)));

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect([FromQuery] string environment, CancellationToken ct)
        => Ok(ApiResponse<PayMongoConnectionResponse>.Ok(await _connections.DisconnectAsync(environment, ct)));
}

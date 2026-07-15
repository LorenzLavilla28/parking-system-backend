using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Guard;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Guard;

namespace ParkingSaaS.Api.Controllers;

/// <summary>Guard-recorded cash payments (only where the location allows cash).</summary>
[Authorize(Policy = AuthorizationPolicies.GuardOrAbove)]
[Route("api/guard/cash-payments")]
public sealed class GuardCashPaymentsController : ApiControllerBase
{
    private readonly IGuardCashPaymentService _cash;

    public GuardCashPaymentsController(IGuardCashPaymentService cash) => _cash = cash;

    [HttpPost]
    public async Task<IActionResult> Record([FromBody] RecordCashPaymentRequest request, CancellationToken ct)
        => Ok(ApiResponse<CashReceiptResponse>.Ok(await _cash.RecordCashPaymentAsync(request, ClientIp, ct)));
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Payments;

namespace ParkingSaaS.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
[Route("api/tenant/payments")]
public sealed class PaymentTrackingController : ApiControllerBase
{
    private readonly IPaymentTrackingService _payments;

    public PaymentTrackingController(IPaymentTrackingService payments) => _payments = payments;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] PaymentQueryRequest request, CancellationToken ct)
        => Ok(ApiResponse<PagedResult<PaymentSummaryResponse>>.Ok(await _payments.SearchAsync(request, ct)));

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] PaymentQueryRequest request, CancellationToken ct)
        => File(await _payments.ExportCsvAsync(request, ct), "text/csv", $"payments-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<PaymentDetailResponse>.Ok(await _payments.GetAsync(id, ct)));
}

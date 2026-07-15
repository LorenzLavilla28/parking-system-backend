using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Api.Auth;
using ParkingSaaS.Application.Reports;
using ParkingSaaS.Contracts.Common;
using ParkingSaaS.Contracts.Reports;

namespace ParkingSaaS.Api.Controllers;

[Authorize(Policy = AuthorizationPolicies.TenantAdmin)]
[Route("api/tenant/dashboard")]
public sealed class DashboardController : ApiControllerBase
{
    private readonly IDashboardReportService _reports;

    public DashboardController(IDashboardReportService reports) => _reports = reports;

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 7, CancellationToken ct = default)
        => Ok(ApiResponse<DashboardReportResponse>.Ok(await _reports.GetAsync(days, ct)));
}

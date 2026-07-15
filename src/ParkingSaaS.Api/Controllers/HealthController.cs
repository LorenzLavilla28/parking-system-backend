using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Infrastructure.Persistence;

namespace ParkingSaaS.Api.Controllers;

/// <summary>Liveness and readiness probes for load balancers and orchestration.</summary>
[AllowAnonymous]
[Route("api/health")]
public sealed class HealthController : ApiControllerBase
{
    /// <summary>Liveness: the process is up and serving requests.</summary>
    [HttpGet]
    public IActionResult Live() => Ok(new { status = "healthy" });

    /// <summary>Readiness: dependencies (database) are reachable.</summary>
    [HttpGet("ready")]
    public async Task<IActionResult> Ready([FromServices] AppDbContext db, CancellationToken ct)
    {
        var canConnect = await db.Database.CanConnectAsync(ct);
        return canConnect
            ? Ok(new { status = "ready", database = "up" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "not_ready", database = "down" });
    }
}

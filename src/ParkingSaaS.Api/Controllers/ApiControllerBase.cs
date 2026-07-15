using Microsoft.AspNetCore.Mvc;

namespace ParkingSaaS.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Best-effort client IP for audit/refresh-token bookkeeping.</summary>
    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}

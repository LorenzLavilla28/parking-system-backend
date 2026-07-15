namespace ParkingSaaS.Api.Middleware;

/// <summary>Adds baseline security response headers to every response.</summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";
        // The API serves JSON only; lock the page down hard.
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        await _next(context);
    }
}

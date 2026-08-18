using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ParkingSaaS.Application.Common.Exceptions;

namespace ParkingSaaS.Api.Middleware;

/// <summary>
/// Translates exceptions into RFC 7807 Problem Details. Client-safe messages are
/// returned to callers while full technical detail stays in the server logs.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title, code, errors) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception ({Code})", code);
        else
            _logger.LogWarning("Handled exception ({Code}): {Message}", code, exception.Message);

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.io/{status}",
            Detail = status >= 500 ? "An unexpected error occurred." : exception.Message,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = httpContext.TraceIdentifier;
        if (errors is not null)
            problem.Extensions["errors"] = errors;

        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem
        });
    }

    private static (int Status, string Title, string Code, IReadOnlyDictionary<string, string[]>? Errors) Map(Exception ex) => ex switch
    {
        ValidationAppException v => (StatusCodes.Status400BadRequest, "Validation failed", v.Code, v.Errors),
        ValidationException fv => (StatusCodes.Status400BadRequest, "Validation failed", "validation_failed",
            fv.Errors.GroupBy(e => e.PropertyName)
                     .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
        NotFoundException n => (StatusCodes.Status404NotFound, "Not found", n.Code, null),
        ConflictException c => (StatusCodes.Status409Conflict, "Conflict", c.Code, null),
        TenantSuspendedException s => (StatusCodes.Status403Forbidden, "Tenant suspended", s.Code, null),
        ForbiddenException f => (StatusCodes.Status403Forbidden, "Forbidden", f.Code, null),
        UnauthorizedAppException u => (StatusCodes.Status401Unauthorized, "Unauthorized", u.Code, null),
        ThrottledException t => (StatusCodes.Status429TooManyRequests, "Too many requests", t.Code, null),
        _ => (StatusCodes.Status500InternalServerError, "Server error", "server_error", null)
    };
}

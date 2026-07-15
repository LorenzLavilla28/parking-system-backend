namespace ParkingSaaS.Application.Common.Exceptions;

/// <summary>Base for application-layer failures that the API maps to Problem Details.</summary>
public abstract class AppException : Exception
{
    public string Code { get; }
    protected AppException(string code, string message) : base(message) => Code = code;
}

public sealed class NotFoundException : AppException
{
    public NotFoundException(string message = "The requested resource was not found.")
        : base("not_found", message) { }
}

public sealed class ConflictException : AppException
{
    public ConflictException(string message) : base("conflict", message) { }
}

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message = "You do not have access to this resource.")
        : base("forbidden", message) { }
}

public sealed class UnauthorizedAppException : AppException
{
    public UnauthorizedAppException(string message = "Invalid credentials.")
        : base("unauthorized", message) { }
}

/// <summary>Signals that a client has been temporarily blocked for too many attempts.</summary>
public sealed class ThrottledException : AppException
{
    public ThrottledException(string message = "Too many attempts. Please try again later.")
        : base("rate_limited", message) { }
}

/// <summary>Aggregates FluentValidation failures into a field → messages map.</summary>
public sealed class ValidationAppException : AppException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationAppException(IReadOnlyDictionary<string, string[]> errors)
        : base("validation_failed", "One or more validation errors occurred.")
        => Errors = errors;
}

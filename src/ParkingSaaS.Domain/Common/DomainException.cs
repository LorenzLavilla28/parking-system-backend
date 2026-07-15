namespace ParkingSaaS.Domain.Common;

/// <summary>
/// Thrown when a domain invariant is violated. Carries a stable
/// <see cref="Code"/> so the API layer can map it to a Problem Details type
/// without leaking internal detail.
/// </summary>
public class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }
}

namespace ParkingSaaS.Contracts.Customer;

/// <summary>Public details of a parking location, shown on the lost-ticket page.</summary>
public sealed record PublicLocationResponse(
    string Slug,
    string Name,
    string? Address);

public sealed record PlateLookupRequest(
    string PlateNumber,
    string? CaptchaToken);

/// <summary>
/// Result of a public plate lookup. On a unique match the public token is
/// returned so the SPA can navigate to the payment page. Multiple matches ask
/// for the ticket code; no match returns a generic not-found.
/// </summary>
public sealed record PlateLookupResponse(
    string Outcome,            // "found" | "multiple" | "not_found"
    string? PublicToken,
    bool CaptchaRequired);

/// <summary>
/// Public, masked view of a session for the payment page. Deliberately excludes
/// customer identity, history, guard details, and internal identifiers.
/// </summary>
public sealed record PublicSessionResponse(
    string MaskedPlate,
    string LocationName,
    DateTimeOffset EntryTime,
    string Status,
    string PaymentStatus,
    decimal? CurrentFee,
    DateTimeOffset? PaidExitDeadline);

using ParkingSaaS.Contracts.Customer;

namespace ParkingSaaS.Application.Customer;

/// <summary>Context about the calling client, supplied by the API for throttling/logging.</summary>
public sealed record PublicClientContext(string ClientKey, string? RemoteIp);

public interface ICustomerPublicService
{
    Task<PublicLocationResponse> GetLocationAsync(string slug, CancellationToken ct);

    Task<PlateLookupResponse> LookupByPlateAsync(
        string slug, PlateLookupRequest request, PublicClientContext client, CancellationToken ct);

    Task<PublicSessionResponse> GetSessionByTokenAsync(string publicToken, CancellationToken ct);
}

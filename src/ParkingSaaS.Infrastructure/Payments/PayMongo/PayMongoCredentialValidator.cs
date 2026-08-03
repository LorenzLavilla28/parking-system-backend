using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;

namespace ParkingSaaS.Infrastructure.Payments.PayMongo;

public sealed class PayMongoCredentialValidator : IPayMongoCredentialValidator
{
    private readonly HttpClient _http;
    private readonly PayMongoOptions _options;
    private readonly ILogger<PayMongoCredentialValidator> _logger;

    public PayMongoCredentialValidator(
        HttpClient http,
        IOptions<PayMongoOptions> options,
        ILogger<PayMongoCredentialValidator> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _http.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
    }

    public async Task<PayMongoCredentialValidationResult> ValidateAsync(
        string secretKey,
        CancellationToken cancellationToken)
    {
        if (!secretKey.StartsWith("sk_live_", StringComparison.Ordinal))
            return new(false, null, "Only live PayMongo secret keys beginning with sk_live_ are accepted.");

        if (!_options.ValidateCredentialsWithProvider)
            return new(true, null, null);

        using var request = new HttpRequestMessage(HttpMethod.Get, "payments?limit=1");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{secretKey}:")));

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return new(true, null, null);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(false, null, "PayMongo rejected this secret key.");

            _logger.LogWarning("PayMongo credential validation returned {Status}.", (int)response.StatusCode);
            return new(false, null, "PayMongo could not validate this key right now.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "PayMongo credential validation failed due to a network error.");
            return new(false, null, "PayMongo could not be reached. Please try again.");
        }
    }
}

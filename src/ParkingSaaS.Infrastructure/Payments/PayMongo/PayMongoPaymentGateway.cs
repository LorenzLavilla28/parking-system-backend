using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Domain.Payments;

namespace ParkingSaaS.Infrastructure.Payments.PayMongo;

/// <summary>
/// PayMongo Hosted Checkout implementation of <see cref="IPaymentGateway"/>.
/// The secret key is used only here (HTTP Basic, key as username) and is never
/// logged. Amounts are sent/received in centavos. Webhook verification is
/// delegated to <see cref="PayMongoSignature"/>.
/// </summary>
public sealed class PayMongoPaymentGateway : IPaymentGateway
{
    private readonly HttpClient _http;
    private readonly PayMongoOptions _options;
    private readonly IPayMongoCredentialsResolver _credentials;
    private readonly ILogger<PayMongoPaymentGateway> _logger;

    public PayMongoPaymentGateway(
        HttpClient http,
        IOptions<PayMongoOptions> options,
        IPayMongoCredentialsResolver credentials,
        ILogger<PayMongoPaymentGateway> logger)
    {
        _http = http;
        _options = options.Value;
        _credentials = credentials;
        _logger = logger;

        _http.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
    }

    public Task<CreateCheckoutResult> CreateCheckoutAsync(CreateCheckoutRequest request, CancellationToken ct)
        => CreateCheckoutInternalAsync(null, request, ct);

    public Task<CreateCheckoutResult> CreateCheckoutAsync(Guid tenantId, CreateCheckoutRequest request, CancellationToken ct)
        => CreateCheckoutInternalAsync(tenantId, request, ct);

    private async Task<CreateCheckoutResult> CreateCheckoutInternalAsync(Guid? tenantId, CreateCheckoutRequest request, CancellationToken ct)
    {
        var credentials = await GetCredentialsAsync(tenantId, ct);
        // Methods offered on the hosted page come from configuration (GCash + QR Ph by
        // default); fall back to a sane set if misconfigured to an empty list.
        var paymentMethodTypes = _options.PaymentMethodTypes is { Length: > 0 }
            ? _options.PaymentMethodTypes
            : ["gcash", "qrph", "card"];

        var body = new
        {
            data = new
            {
                attributes = new
                {
                    line_items = new[]
                    {
                        new
                        {
                            name = request.LineItemName,
                            amount = ToCentavos(request.Amount),
                            currency = request.Currency,
                            quantity = 1
                        }
                    },
                    payment_method_types = paymentMethodTypes,
                    description = request.Description,
                    reference_number = request.ReferenceNumber,
                    success_url = request.SuccessUrl,
                    cancel_url = request.CancelUrl,
                    send_email_receipt = false,
                    metadata = new { reference = request.ReferenceNumber }
                }
            }
        };

        using var httpRequest = CreateRequest(HttpMethod.Post, "checkout_sessions", credentials);
        httpRequest.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(httpRequest, ct);
        await EnsureSuccessAsync(response, "create checkout", ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var data = doc.RootElement.GetProperty("data");
        var id = data.GetProperty("id").GetString()!;
        var attributes = data.GetProperty("attributes");
        var checkoutUrl = attributes.TryGetProperty("checkout_url", out var url) ? url.GetString()! : string.Empty;

        return new CreateCheckoutResult(id, checkoutUrl, id, credentials.PayMongoAccountId);
    }

    public Task<PaymentStatusResult> GetPaymentStatusAsync(string providerReference, CancellationToken ct)
        => GetPaymentStatusInternalAsync(null, providerReference, ct);

    public Task<PaymentStatusResult> GetPaymentStatusAsync(Guid tenantId, string providerReference, CancellationToken ct)
        => GetPaymentStatusInternalAsync(tenantId, providerReference, ct);

    private async Task<PaymentStatusResult> GetPaymentStatusInternalAsync(Guid? tenantId, string providerReference, CancellationToken ct)
    {
        var credentials = await GetCredentialsAsync(tenantId, ct);
        using var response = await _http.SendAsync(
            CreateRequest(HttpMethod.Get, $"checkout_sessions/{providerReference}", credentials), ct);
        await EnsureSuccessAsync(response, "get checkout", ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var attributes = doc.RootElement.GetProperty("data").GetProperty("attributes");

        // A checkout is paid once it has a settled payment.
        if (attributes.TryGetProperty("payments", out var payments) &&
            payments.ValueKind == JsonValueKind.Array && payments.GetArrayLength() > 0)
        {
            foreach (var payment in payments.EnumerateArray())
            {
                var pAttr = payment.GetProperty("attributes");
                var status = pAttr.TryGetProperty("status", out var s) ? s.GetString() : null;
                if (status == "paid")
                {
                    return new PaymentStatusResult(
                        PaymentStatus.Paid,
                        payment.GetProperty("id").GetString(),
                        FromCentavos(pAttr),
                        pAttr.TryGetProperty("currency", out var c) ? c.GetString() : null,
                        ReadPaymentMethod(pAttr));
                }
            }
        }

        // An expired checkout carries no payment and cannot be completed — report it as
        // Expired so reconciliation can close it out and free the parking session.
        var checkoutStatus = attributes.TryGetProperty("status", out var cs) ? cs.GetString() : null;
        if (checkoutStatus == "expired")
            return new PaymentStatusResult(PaymentStatus.Expired, null, null, null, null);

        return new PaymentStatusResult(PaymentStatus.Pending, null, null, null, null);
    }

    public Task ExpireCheckoutAsync(string providerReference, CancellationToken ct)
        => ExpireCheckoutInternalAsync(null, providerReference, ct);

    public Task ExpireCheckoutAsync(Guid tenantId, string providerReference, CancellationToken ct)
        => ExpireCheckoutInternalAsync(tenantId, providerReference, ct);

    private async Task ExpireCheckoutInternalAsync(Guid? tenantId, string providerReference, CancellationToken ct)
    {
        var credentials = await GetCredentialsAsync(tenantId, ct);
        using var response = await _http.SendAsync(
            CreateRequest(HttpMethod.Post, $"checkout_sessions/{providerReference}/expire", credentials), ct);
        if (response.IsSuccessStatusCode || (int)response.StatusCode == 404)
            return;

        var detail = await response.Content.ReadAsStringAsync(ct);
        _logger.LogError("PayMongo expire checkout failed with {Status} for {Ref}.", (int)response.StatusCode, providerReference);
        throw new InvalidOperationException($"PayMongo expire checkout failed ({(int)response.StatusCode}): {detail}");
    }

    public Task<WebhookVerificationResult> VerifyWebhookAsync(string rawPayload, string signatureHeader, CancellationToken ct)
        => VerifyWebhookInternalAsync(null, rawPayload, signatureHeader, ct);

    public Task<WebhookVerificationResult> VerifyWebhookAsync(Guid tenantId, string rawPayload, string signatureHeader, CancellationToken ct)
        => VerifyWebhookInternalAsync(tenantId, rawPayload, signatureHeader, ct);

    private async Task<WebhookVerificationResult> VerifyWebhookInternalAsync(
        Guid? tenantId,
        string rawPayload,
        string signatureHeader,
        CancellationToken ct)
    {
        var credentials = await GetCredentialsAsync(tenantId, ct);
        if (!PayMongoSignature.Verify(rawPayload, signatureHeader, credentials.WebhookSecret, out _))
            return Invalid();

        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            var eventData = doc.RootElement.GetProperty("data");
            var eventId = eventData.GetProperty("id").GetString();
            var eventAttr = eventData.GetProperty("attributes");
            var eventType = eventAttr.GetProperty("type").GetString();

            var resource = eventAttr.GetProperty("data");
            var resourceAttr = resource.GetProperty("attributes");

            // checkout_session.payment.paid carries the checkout id and its payment(s).
            var mapped = eventType is "checkout_session.payment.paid" or "payment.paid"
                ? PaymentStatus.Paid
                : (PaymentStatus?)null;

            string? checkoutId = null, paymentId = null, method = null, currency = null;
            decimal? amount = null;

            if (eventType!.StartsWith("checkout_session", StringComparison.Ordinal))
            {
                checkoutId = resource.GetProperty("id").GetString();
                if (resourceAttr.TryGetProperty("payments", out var payments) &&
                    payments.ValueKind == JsonValueKind.Array && payments.GetArrayLength() > 0)
                {
                    var p = payments[0];
                    var pAttr = p.GetProperty("attributes");
                    paymentId = p.GetProperty("id").GetString();
                    amount = FromCentavos(pAttr);
                    currency = pAttr.TryGetProperty("currency", out var c) ? c.GetString() : null;
                    method = ReadPaymentMethod(pAttr);
                }
            }
            else // payment.paid
            {
                paymentId = resource.GetProperty("id").GetString();
                amount = FromCentavos(resourceAttr);
                currency = resourceAttr.TryGetProperty("currency", out var c) ? c.GetString() : null;
                method = ReadPaymentMethod(resourceAttr);
            }

            return new WebhookVerificationResult(
                true, eventId, eventType, checkoutId, paymentId, mapped, amount, currency, method);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse a signature-valid PayMongo webhook payload.");
            return new WebhookVerificationResult(true, null, null, null, null, null, null, null, null);
        }
    }

    private async Task<ResolvedPayMongoCredentials> GetCredentialsAsync(Guid? tenantId, CancellationToken ct)
        => await _credentials.ResolveAsync(tenantId, ct)
           ?? throw new InvalidOperationException("PayMongo is not configured for this tenant.");

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        ResolvedPayMongoCredentials credentials)
    {
        var request = new HttpRequestMessage(method, path);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.SecretKey}:"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        return request;
    }

    private static WebhookVerificationResult Invalid()
        => new(false, null, null, null, null, null, null, null, null);

    private static long ToCentavos(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static decimal? FromCentavos(JsonElement attributes)
        => attributes.TryGetProperty("amount", out var a) && a.TryGetInt64(out var centavos)
            ? centavos / 100m
            : null;

    private static string? ReadPaymentMethod(JsonElement attributes)
        => attributes.TryGetProperty("source", out var src) && src.TryGetProperty("type", out var t)
            ? t.GetString()
            : null;

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        // Log status only — never the secret-bearing request or full body at info level.
        var detail = await response.Content.ReadAsStringAsync(ct);
        _logger.LogError("PayMongo {Action} failed with {Status}.", action, (int)response.StatusCode);
        throw new InvalidOperationException($"PayMongo {action} failed ({(int)response.StatusCode}): {detail}");
    }

}

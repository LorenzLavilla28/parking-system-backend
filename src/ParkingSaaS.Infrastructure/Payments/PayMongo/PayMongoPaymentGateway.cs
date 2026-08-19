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
/// PayMongo payment implementation of <see cref="IPaymentGateway"/>. Hosted
/// Checkout remains supported for existing callers; new parking payments can
/// use the Payment Intent + dynamic QR Ph flow.
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
        httpRequest.Headers.TryAddWithoutValidation("Idempotency-Key", request.IdempotencyKey);
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

    public Task<CreateCheckoutResult> CreateDynamicQrAsync(CreateCheckoutRequest request, CancellationToken ct)
        => CreateDynamicQrInternalAsync(null, request, ct);

    public Task<CreateCheckoutResult> CreateDynamicQrAsync(Guid tenantId, CreateCheckoutRequest request, CancellationToken ct)
        => CreateDynamicQrInternalAsync(tenantId, request, ct);

    private async Task<CreateCheckoutResult> CreateDynamicQrInternalAsync(
        Guid? tenantId, CreateCheckoutRequest request, CancellationToken ct)
    {
        var credentials = await GetCredentialsAsync(tenantId, ct);
        var amount = ToCentavos(request.Amount);

        var intentBody = new
        {
            data = new
            {
                attributes = new
                {
                    amount,
                    currency = request.Currency,
                    payment_method_allowed = new[] { "qrph" },
                    description = request.Description,
                    metadata = new { reference = request.ReferenceNumber }
                }
            }
        };

        using var intentRequest = CreateRequest(HttpMethod.Post, "payment_intents", credentials);
        intentRequest.Headers.TryAddWithoutValidation("Idempotency-Key", $"{request.IdempotencyKey}-intent");
        intentRequest.Content = JsonContent.Create(intentBody);
        using var intentResponse = await _http.SendAsync(intentRequest, ct);
        await EnsureSuccessAsync(intentResponse, "create QR payment intent", ct);

        using var intentDoc = JsonDocument.Parse(await intentResponse.Content.ReadAsStringAsync(ct));
        var intentData = intentDoc.RootElement.GetProperty("data");
        var intentId = intentData.GetProperty("id").GetString()!;
        var clientKey = intentData.GetProperty("attributes").GetProperty("client_key").GetString()!;

        var methodBody = new
        {
            data = new
            {
                attributes = new
                {
                    type = "qrph",
                    expiry_seconds = 1800
                }
            }
        };

        using var methodRequest = CreateRequest(HttpMethod.Post, "payment_methods", credentials);
        methodRequest.Headers.TryAddWithoutValidation("Idempotency-Key", $"{request.IdempotencyKey}-method");
        methodRequest.Content = JsonContent.Create(methodBody);
        using var methodResponse = await _http.SendAsync(methodRequest, ct);
        await EnsureSuccessAsync(methodResponse, "create QR payment method", ct);

        using var methodDoc = JsonDocument.Parse(await methodResponse.Content.ReadAsStringAsync(ct));
        var methodId = methodDoc.RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var attachBody = new
        {
            data = new
            {
                attributes = new
                {
                    payment_method = methodId,
                    client_key = clientKey
                }
            }
        };

        using var attachRequest = CreateRequest(HttpMethod.Post, $"payment_intents/{intentId}/attach", credentials);
        attachRequest.Headers.TryAddWithoutValidation("Idempotency-Key", $"{request.IdempotencyKey}-attach");
        attachRequest.Content = JsonContent.Create(attachBody);
        using var attachResponse = await _http.SendAsync(attachRequest, ct);
        await EnsureSuccessAsync(attachResponse, "attach QR payment method", ct);

        using var attachDoc = JsonDocument.Parse(await attachResponse.Content.ReadAsStringAsync(ct));
        var attachAttributes = attachDoc.RootElement.GetProperty("data").GetProperty("attributes");
        var imageUrl = attachAttributes
            .GetProperty("next_action")
            .GetProperty("code")
            .GetProperty("image_url")
            .GetString();

        if (string.IsNullOrWhiteSpace(imageUrl))
            throw new InvalidOperationException("PayMongo did not return a dynamic QR image.");

        return new CreateCheckoutResult(intentId, string.Empty, intentId, credentials.PayMongoAccountId, imageUrl);
    }

    public Task<string?> GetQrCodeImageAsync(string providerReference, CancellationToken ct)
        => GetQrCodeImageInternalAsync(null, providerReference, ct);

    public Task<string?> GetQrCodeImageAsync(Guid tenantId, string providerReference, CancellationToken ct)
        => GetQrCodeImageInternalAsync(tenantId, providerReference, ct);

    private async Task<string?> GetQrCodeImageInternalAsync(Guid? tenantId, string providerReference, CancellationToken ct)
    {
        if (!providerReference.StartsWith("pi_", StringComparison.OrdinalIgnoreCase))
            return null;

        var credentials = await GetCredentialsAsync(tenantId, ct);
        using var response = await _http.SendAsync(
            CreateRequest(HttpMethod.Get, $"payment_intents/{providerReference}", credentials), ct);
        await EnsureSuccessAsync(response, "get QR payment intent", ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var attributes = doc.RootElement.GetProperty("data").GetProperty("attributes");
        return attributes.TryGetProperty("next_action", out var nextAction) &&
               nextAction.TryGetProperty("code", out var code) &&
               code.TryGetProperty("image_url", out var image)
            ? image.GetString()
            : null;
    }

    public Task<PaymentStatusResult> GetPaymentStatusAsync(string providerReference, CancellationToken ct)
        => GetPaymentStatusInternalAsync(null, providerReference, ct);

    public Task<PaymentStatusResult> GetPaymentStatusAsync(Guid tenantId, string providerReference, CancellationToken ct)
        => GetPaymentStatusInternalAsync(tenantId, providerReference, ct);

    private async Task<PaymentStatusResult> GetPaymentStatusInternalAsync(Guid? tenantId, string providerReference, CancellationToken ct)
    {
        var credentials = await GetCredentialsAsync(tenantId, ct);
        if (providerReference.StartsWith("pi_", StringComparison.OrdinalIgnoreCase))
            return await GetDynamicQrPaymentStatusAsync(credentials, providerReference, ct);

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

    private async Task<PaymentStatusResult> GetDynamicQrPaymentStatusAsync(
        ResolvedPayMongoCredentials credentials, string providerReference, CancellationToken ct)
    {
        using var response = await _http.SendAsync(
            CreateRequest(HttpMethod.Get, $"payment_intents/{providerReference}", credentials), ct);
        await EnsureSuccessAsync(response, "get QR payment status", ct);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var attributes = doc.RootElement.GetProperty("data").GetProperty("attributes");
        var intentStatus = attributes.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : null;

        if (attributes.TryGetProperty("payments", out var payments) &&
            payments.ValueKind == JsonValueKind.Array)
        {
            foreach (var payment in payments.EnumerateArray())
            {
                var paymentAttributes = payment.TryGetProperty("attributes", out var pa) ? pa : default;
                if (paymentAttributes.ValueKind != JsonValueKind.Object)
                    continue;

                var paymentStatus = paymentAttributes.TryGetProperty("status", out var ps)
                    ? ps.GetString()
                    : null;
                if (paymentStatus == "paid")
                {
                    return new PaymentStatusResult(
                        PaymentStatus.Paid,
                        payment.TryGetProperty("id", out var paymentId) ? paymentId.GetString() : null,
                        FromCentavos(paymentAttributes),
                        paymentAttributes.TryGetProperty("currency", out var currency) ? currency.GetString() : null,
                        ReadPaymentMethod(paymentAttributes));
                }
            }
        }

        if (intentStatus == "succeeded")
            return new PaymentStatusResult(
                PaymentStatus.Paid,
                null,
                FromCentavos(attributes),
                attributes.TryGetProperty("currency", out var succeededCurrency) ? succeededCurrency.GetString() : null,
                "qrph");

        if (intentStatus is "awaiting_payment_method" or "cancelled" or "failed")
            return new PaymentStatusResult(
                intentStatus == "failed" ? PaymentStatus.Failed : PaymentStatus.Expired,
                null, null, null, null);

        return new PaymentStatusResult(PaymentStatus.Pending, null, null, null, "qrph");
    }

    public Task ExpireCheckoutAsync(string providerReference, CancellationToken ct)
        => ExpireCheckoutInternalAsync(null, providerReference, ct);

    public Task ExpireCheckoutAsync(Guid tenantId, string providerReference, CancellationToken ct)
        => ExpireCheckoutInternalAsync(tenantId, providerReference, ct);

    private async Task ExpireCheckoutInternalAsync(Guid? tenantId, string providerReference, CancellationToken ct)
    {
        // Dynamic QR Payment Intents expire their attached QR code at PayMongo.
        // Closing the local attempt is sufficient; there is no checkout-session
        // expire endpoint for this flow.
        if (providerReference.StartsWith("pi_", StringComparison.OrdinalIgnoreCase))
            return;

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

            // Hosted Checkout events carry a checkout-session id. Direct Payment
            // Intent events carry payment_intent_id on the payment resource.
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
                checkoutId = resourceAttr.TryGetProperty("payment_intent_id", out var intentId)
                    ? intentId.GetString()
                    : null;
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

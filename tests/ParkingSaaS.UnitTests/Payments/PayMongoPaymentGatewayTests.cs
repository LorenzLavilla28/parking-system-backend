using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Infrastructure.Payments.PayMongo;
using Xunit;

namespace ParkingSaaS.UnitTests.Payments;

/// <summary>
/// Verifies the checkout request the gateway sends to PayMongo — in particular that the
/// configured payment methods (GCash + QR Ph by default) are what gets offered on the
/// hosted page. Uses a stub handler so no real HTTP call is made.
/// </summary>
public sealed class PayMongoPaymentGatewayTests
{
    private const string CheckoutResponse =
        "{\"data\":{\"id\":\"cs_123\",\"attributes\":{\"checkout_url\":\"https://checkout.paymongo.com/cs_123\"}}}";

    private static readonly CreateCheckoutRequest Request = new(
        Currency: "PHP",
        Amount: 90.00m,
        Description: "Parking payment (ABC123)",
        LineItemName: "Parking fee",
        ReferenceNumber: "ref-1",
        SuccessUrl: "https://app/s",
        CancelUrl: "https://app/c",
        IdempotencyKey: "quote-1");

    private static (PayMongoPaymentGateway Gateway, CapturingHandler Handler) Build(PayMongoOptions options)
    {
        var handler = new CapturingHandler(CheckoutResponse);
        var http = new HttpClient(handler);
        var gateway = new PayMongoPaymentGateway(
            http, Options.Create(options), NullLogger<PayMongoPaymentGateway>.Instance);
        return (gateway, handler);
    }

    [Fact]
    public async Task Checkout_offers_gcash_and_qrph_by_default()
    {
        var (gateway, handler) = Build(new PayMongoOptions { SecretKey = "sk_test_x" });

        await gateway.CreateCheckoutAsync(Request, CancellationToken.None);

        var types = MethodTypes(handler.LastBody!);
        types.Should().Contain("gcash").And.Contain("qrph");
    }

    [Fact]
    public async Task Checkout_uses_configured_payment_methods()
    {
        var (gateway, handler) = Build(new PayMongoOptions
        {
            SecretKey = "sk_test_x",
            PaymentMethodTypes = ["gcash", "qrph"]
        });

        await gateway.CreateCheckoutAsync(Request, CancellationToken.None);

        MethodTypes(handler.LastBody!).Should().Equal("gcash", "qrph");
    }

    [Fact]
    public async Task Checkout_falls_back_when_methods_misconfigured_to_empty()
    {
        var (gateway, handler) = Build(new PayMongoOptions
        {
            SecretKey = "sk_test_x",
            PaymentMethodTypes = []
        });

        await gateway.CreateCheckoutAsync(Request, CancellationToken.None);

        MethodTypes(handler.LastBody!).Should().Contain("gcash").And.Contain("qrph");
    }

    private static List<string> MethodTypes(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement
            .GetProperty("data").GetProperty("attributes").GetProperty("payment_method_types")
            .EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        public string? LastBody { get; private set; }

        public CapturingHandler(string responseJson) => _responseJson = responseJson;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}

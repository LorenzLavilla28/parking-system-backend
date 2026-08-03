using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Infrastructure.Payments.PayMongo;
using Xunit;

namespace ParkingSaaS.UnitTests.Payments;

public sealed class PayMongoCredentialValidatorTests
{
    [Fact]
    public async Task Rejects_test_secret_keys_without_contacting_paymongo()
    {
        var handler = new CountingHandler(HttpStatusCode.OK);
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync("sk_test_not_allowed", CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("sk_live_");
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task Accepts_a_live_key_that_paymongo_validates()
    {
        var handler = new CountingHandler(HttpStatusCode.OK);
        var validator = CreateValidator(handler);

        var result = await validator.ValidateAsync("sk_live_valid", CancellationToken.None);

        result.IsValid.Should().BeTrue();
        handler.RequestCount.Should().Be(1);
    }

    private static PayMongoCredentialValidator CreateValidator(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler),
            Options.Create(new PayMongoOptions()),
            NullLogger<PayMongoCredentialValidator>.Instance);

    private sealed class CountingHandler(HttpStatusCode responseStatus) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(responseStatus));
        }
    }
}

using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Abstractions;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Emails;
using ParkingSaaS.Application.Pricing;
using ParkingSaaS.Application.Payments;
using ParkingSaaS.Contracts.Realtime;
using ParkingSaaS.Domain.Emails;
using ParkingSaaS.Domain.Payments;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.Sessions;
using ParkingSaaS.Domain.Users;

namespace ParkingSaaS.UnitTests.Common;

/// <summary>Builds a real <see cref="EmailQueue"/> over a test DbContext (default options).</summary>
internal static class TestEmail
{
    public static EmailQueue Queue(IApplicationDbContext db, EmailOptions? options = null)
        => new(db, Options.Create(options ?? new EmailOptions()));
}

/// <summary>Email transport the test controls: succeeds by default, can be told to throw.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    public bool ShouldThrow { get; set; }
    public string FailureMessage { get; set; } = "email provider unavailable";
    public List<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (ShouldThrow) throw new InvalidOperationException(FailureMessage);
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>Configurable <see cref="ICurrentUser"/> test double.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; } = true;
    public Guid? UserId { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public IReadOnlyCollection<RoleType> Roles { get; set; } = Array.Empty<RoleType>();
}

/// <summary>Records the realtime signals a service broadcasts so tests can assert them.</summary>
public sealed class FakeSessionRealtimeNotifier : ISessionRealtimeNotifier
{
    public sealed record Call(Guid TenantId, Guid ParkingLocationId, SessionRealtimeEvent Event);

    public List<Call> Calls { get; } = new();
    public Call? Last => Calls.Count > 0 ? Calls[^1] : null;

    public Task SessionChangedAsync(
        Guid tenantId, Guid parkingLocationId, SessionRealtimeEvent evt, CancellationToken ct = default)
    {
        Calls.Add(new Call(tenantId, parkingLocationId, evt));
        return Task.CompletedTask;
    }
}

/// <summary>Stub QR generator; returns a fixed data URI without rendering.</summary>
public sealed class FakeQrCodeGenerator : IQrCodeGenerator
{
    public string LastContent { get; private set; } = string.Empty;
    public string GeneratePngDataUri(string content)
    {
        LastContent = content;
        return "data:image/png;base64,QQ==";
    }
}

/// <summary>CAPTCHA verifier whose result is controlled by the test.</summary>
public sealed class FakeCaptchaVerifier : ICaptchaVerifier
{
    public bool Result { get; set; } = true;
    public Task<bool> VerifyAsync(string? captchaToken, string? remoteIp, CancellationToken ct)
        => Task.FromResult(Result);
}

/// <summary>Throttle whose decision and recorded calls the test controls/inspects.</summary>
public sealed class FakeLookupThrottle : ILookupThrottle
{
    public ThrottleDecision Decision { get; set; } = new(false, false, 0);
    public int Failures { get; private set; }
    public int Successes { get; private set; }

    public ThrottleDecision Evaluate(string clientKey) => Decision;
    public void RegisterFailure(string clientKey) => Failures++;
    public void RegisterSuccess(string clientKey) => Successes++;
}

/// <summary>Rate-plan resolver returning a fixed version id (null by default).</summary>
public sealed class FakeActiveRatePlanResolver : IActiveRatePlanResolver
{
    public Guid? VersionId { get; set; }
    public Task<Guid?> ResolveActiveVersionIdAsync(Guid parkingLocationId, DateTimeOffset at, CancellationToken ct)
        => Task.FromResult(VersionId);
}

/// <summary>Session pricing service returning a fixed result (null by default).</summary>
public sealed class FakeSessionPricingService : ISessionPricingService
{
    public FeeCalculationResult? Result { get; set; }
    public Task<FeeCalculationResult?> CalculateAsync(
        ParkingSession session, DateTimeOffset at, DiscountInput? discount, CancellationToken ct)
        => Task.FromResult(Result);

    public Task<int> GetPaidExitGraceMinutesAsync(ParkingSession session, CancellationToken ct)
        => Task.FromResult(15);

    public Task<DateTimeOffset> GetPaidExitDeadlineAsync(ParkingSession session, DateTimeOffset paidAt, CancellationToken ct)
        => Task.FromResult(paidAt.AddMinutes(15));
}

/// <summary>Tenant PayMongo credential resolver used by payment-service tests.</summary>
public sealed class FakePayMongoCredentialsResolver : IPayMongoCredentialsResolver
{
    public ResolvedPayMongoCredentials? Result { get; set; } =
        new("sk_live_fake", "whsec_live_fake", "acct_live_fake");

    public Task<ResolvedPayMongoCredentials?> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken)
        => Task.FromResult(Result);

    public void Invalidate(Guid tenantId) { }
}

/// <summary>Payment gateway whose responses the test controls and whose calls it inspects.</summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    public CreateCheckoutResult CheckoutResult { get; set; } =
        new("cs_test_123", "https://checkout.paymongo.com/cs_test_123", "cs_test_123");
    public PaymentStatusResult StatusResult { get; set; } =
        new(PaymentStatus.Pending, null, null, null, null);
    public WebhookVerificationResult VerificationResult { get; set; } =
        new(true, "evt_1", "checkout_session.payment.paid", "cs_test_123", "pay_1", PaymentStatus.Paid, 90m, "PHP", "card");

    public int CreateCalls { get; private set; }
    public int ExpireCalls { get; private set; }
    public CreateCheckoutRequest? LastCreateRequest { get; private set; }

    public Task<CreateCheckoutResult> CreateCheckoutAsync(CreateCheckoutRequest request, CancellationToken ct)
    {
        CreateCalls++;
        LastCreateRequest = request;
        return Task.FromResult(CheckoutResult);
    }

    public Task<CreateCheckoutResult> CreateCheckoutAsync(Guid tenantId, CreateCheckoutRequest request, CancellationToken ct)
        => CreateCheckoutAsync(request, ct);

    public Task<PaymentStatusResult> GetPaymentStatusAsync(string providerReference, CancellationToken ct)
        => Task.FromResult(StatusResult);

    public Task<PaymentStatusResult> GetPaymentStatusAsync(Guid tenantId, string providerReference, CancellationToken ct)
        => GetPaymentStatusAsync(providerReference, ct);

    public Task ExpireCheckoutAsync(string providerReference, CancellationToken ct)
    {
        ExpireCalls++;
        return Task.CompletedTask;
    }

    public Task ExpireCheckoutAsync(Guid tenantId, string providerReference, CancellationToken ct)
        => ExpireCheckoutAsync(providerReference, ct);

    public Task<WebhookVerificationResult> VerifyWebhookAsync(string rawPayload, string signatureHeader, CancellationToken ct)
        => Task.FromResult(VerificationResult);

    public Task<WebhookVerificationResult> VerifyWebhookAsync(Guid tenantId, string rawPayload, string signatureHeader, CancellationToken ct)
        => VerifyWebhookAsync(rawPayload, signatureHeader, ct);
}

using FluentAssertions;
using ParkingSaaS.Domain.Common;
using ParkingSaaS.Domain.Payments;
using Xunit;

namespace ParkingSaaS.UnitTests.Domain;

public sealed class PaymentTests
{
    private static Payment NewPending()
        => Payment.CreateOnlinePending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PHP", 90m, "hash", "prot", "key");

    [Fact]
    public void Online_payment_starts_pending()
        => NewPending().Status.Should().Be(PaymentStatus.Pending);

    [Fact]
    public void Zero_or_negative_amount_is_rejected()
    {
        var act = () => Payment.CreateOnlinePending(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PHP", 0m, "h", "p", "k");
        act.Should().Throw<DomainException>().Which.Code.Should().Be("payment.amount_invalid");
    }

    [Fact]
    public void Mark_paid_sets_provider_details()
    {
        var payment = NewPending();
        var paidAt = DateTimeOffset.UtcNow;
        payment.MarkPaid("pay_1", "gcash", paidAt);

        payment.Status.Should().Be(PaymentStatus.Paid);
        payment.ProviderPaymentId.Should().Be("pay_1");
        payment.PaymentMethod.Should().Be("gcash");
        payment.PaidAt.Should().Be(paidAt);
    }

    [Fact]
    public void Mark_paid_is_idempotent()
    {
        var payment = NewPending();
        payment.MarkPaid("pay_1", "card", DateTimeOffset.UtcNow);
        payment.MarkPaid("pay_2", "card", DateTimeOffset.UtcNow.AddMinutes(1));

        // The first settlement wins; history is preserved.
        payment.ProviderPaymentId.Should().Be("pay_1");
    }

    [Fact]
    public void Cannot_pay_a_cancelled_payment()
    {
        var payment = NewPending();
        payment.Cancel();
        var act = () => payment.MarkPaid("pay_1", "card", DateTimeOffset.UtcNow);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("payment.invalid_transition");
    }

    [Fact]
    public void Failed_and_expired_only_apply_to_open_payments()
    {
        var paid = NewPending();
        paid.MarkPaid("p", "card", DateTimeOffset.UtcNow);
        paid.MarkFailed();
        paid.Status.Should().Be(PaymentStatus.Paid, "a settled payment cannot be failed");
    }

    [Fact]
    public void Cash_payment_is_created_already_paid()
    {
        var cash = Payment.CreateCashPaid(Guid.NewGuid(), Guid.NewGuid(), null, "PHP", 70m, "h", "p", DateTimeOffset.UtcNow, "CR-20260624-J7K4P2", Guid.NewGuid());
        cash.Status.Should().Be(PaymentStatus.Paid);
        cash.Provider.Should().Be(PaymentProvider.Cash);
        cash.PaymentMethod.Should().Be("cash");
        cash.ReceiptNumber.Should().Be("CR-20260624-J7K4P2");
    }
}

public sealed class WebhookEventTests
{
    [Fact]
    public void New_event_is_received_then_processed()
    {
        var e = new WebhookEvent(PaymentProvider.PayMongo, "evt_1", "checkout_session.payment.paid", "hash", DateTimeOffset.UtcNow);
        e.ProcessingStatus.Should().Be(WebhookProcessingStatus.Received);

        var tenantId = Guid.NewGuid();
        e.MarkProcessed(DateTimeOffset.UtcNow, tenantId);
        e.ProcessingStatus.Should().Be(WebhookProcessingStatus.Processed);
        e.TenantId.Should().Be(tenantId);
        e.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public void Failure_reason_is_truncated()
    {
        var e = new WebhookEvent(PaymentProvider.PayMongo, "evt_1", "x", "h", DateTimeOffset.UtcNow);
        e.MarkFailed(DateTimeOffset.UtcNow, new string('x', 5000));
        e.FailureReason!.Length.Should().Be(1000);
    }
}

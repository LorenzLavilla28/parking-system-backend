using FluentAssertions;
using ParkingSaaS.Domain.Common;
using ParkingSaaS.Domain.Emails;
using Xunit;

namespace ParkingSaaS.UnitTests.Domain;

public sealed class EmailMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private static EmailMessage Create(int maxAttempts = 5) => EmailMessage.Create(
        EmailKind.PaymentReceipt, "buyer@example.com", null, "Receipt", "<p>hi</p>", "hi", Now, Guid.NewGuid(), maxAttempts);

    [Fact]
    public void New_message_is_pending_and_immediately_due()
    {
        var m = Create();
        m.Status.Should().Be(EmailStatus.Pending);
        m.AttemptCount.Should().Be(0);
        m.IsDue(Now).Should().BeTrue();
    }

    [Fact]
    public void Blank_recipient_is_rejected()
    {
        var act = () => EmailMessage.Create(EmailKind.UserWelcome, "  ", null, "s", "h", "t", Now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("email.recipient_required");
    }

    [Fact]
    public void MarkSent_completes_the_message()
    {
        var m = Create();
        m.MarkSent(Now);
        m.Status.Should().Be(EmailStatus.Sent);
        m.SentAt.Should().Be(Now);
        m.IsDue(Now.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void MarkFailed_reschedules_with_backoff_and_stays_pending()
    {
        var m = Create(maxAttempts: 3);
        m.MarkFailed(Now, "provider timeout");

        m.Status.Should().Be(EmailStatus.Pending);
        m.AttemptCount.Should().Be(1);
        m.LastError.Should().Be("provider timeout");
        m.NextAttemptAt.Should().BeAfter(Now);      // backoff applied
        m.IsDue(Now).Should().BeFalse();             // not due until backoff elapses
        m.IsDue(m.NextAttemptAt).Should().BeTrue();
    }

    [Fact]
    public void MarkFailed_dead_letters_after_max_attempts()
    {
        var m = Create(maxAttempts: 2);
        m.MarkFailed(Now, "err 1");                  // attempt 1 → retry
        m.Status.Should().Be(EmailStatus.Pending);
        m.MarkFailed(Now, "err 2");                  // attempt 2 → dead-letter

        m.Status.Should().Be(EmailStatus.Failed);
        m.AttemptCount.Should().Be(2);
        m.IsDue(Now.AddDays(1)).Should().BeFalse();  // never retried again
    }
}

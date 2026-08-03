using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Emails;
using ParkingSaaS.Domain.Emails;
using ParkingSaaS.Infrastructure.Persistence;
using ParkingSaaS.UnitTests.Common;
using Xunit;

namespace ParkingSaaS.UnitTests.Emails;

public sealed class EmailDispatchServiceTests
{
    private readonly MutableTenantContext _tenant = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeEmailSender _sender = new();
    private readonly AppDbContext _db;
    private readonly EmailDispatchService _service;

    public EmailDispatchServiceTests()
    {
        _db = InMemoryDb.Create(_tenant);
        _service = new EmailDispatchService(
            _db, _sender, _clock,
            Options.Create(new EmailOptions { Enabled = true, DispatchBatchSize = 25 }),
            NullLogger<EmailDispatchService>.Instance);
    }

    private EmailMessage Enqueue(DateTimeOffset? nextAttempt = null, int maxAttempts = 5)
    {
        var m = EmailMessage.Create(
            EmailKind.UserWelcome, "staff@example.com", "Staff", "Welcome", "<p>hi</p>", "hi",
            nextAttempt ?? _clock.UtcNow, Guid.NewGuid(), maxAttempts);
        _db.Emails.Add(m);
        _db.SaveChanges();
        return m;
    }

    [Fact]
    public async Task Due_pending_messages_are_sent_and_marked()
    {
        Enqueue();

        var summary = await _service.DispatchDueAsync(CancellationToken.None);

        summary.Sent.Should().Be(1);
        _sender.Sent.Should().ContainSingle();
        (await _db.Emails.SingleAsync()).Status.Should().Be(EmailStatus.Sent);
    }

    [Fact]
    public async Task Messages_not_yet_due_are_skipped()
    {
        Enqueue(nextAttempt: _clock.UtcNow.AddMinutes(10));

        var summary = await _service.DispatchDueAsync(CancellationToken.None);

        summary.Attempted.Should().Be(0);
        _sender.Sent.Should().BeEmpty();
        (await _db.Emails.SingleAsync()).Status.Should().Be(EmailStatus.Pending);
    }

    [Fact]
    public async Task Disabled_delivery_leaves_due_messages_pending()
    {
        Enqueue();
        var disabledService = new EmailDispatchService(
            _db, _sender, _clock,
            Options.Create(new EmailOptions { Enabled = false, DispatchBatchSize = 25 }),
            NullLogger<EmailDispatchService>.Instance);

        var summary = await disabledService.DispatchDueAsync(CancellationToken.None);

        summary.Attempted.Should().Be(0);
        _sender.Sent.Should().BeEmpty();
        (await _db.Emails.SingleAsync()).Status.Should().Be(EmailStatus.Pending);
    }

    [Fact]
    public async Task A_send_failure_is_retried_not_dead_lettered()
    {
        Enqueue(maxAttempts: 3);
        _sender.ShouldThrow = true;

        var summary = await _service.DispatchDueAsync(CancellationToken.None);

        summary.Failed.Should().Be(1);
        summary.DeadLettered.Should().Be(0);
        var m = await _db.Emails.SingleAsync();
        m.Status.Should().Be(EmailStatus.Pending);
        m.AttemptCount.Should().Be(1);
        m.NextAttemptAt.Should().BeAfter(_clock.UtcNow);
    }

    [Fact]
    public async Task A_message_is_dead_lettered_after_exhausting_attempts()
    {
        var m = Enqueue(maxAttempts: 1);
        _sender.ShouldThrow = true;

        var summary = await _service.DispatchDueAsync(CancellationToken.None);

        summary.DeadLettered.Should().Be(1);
        (await _db.Emails.SingleAsync()).Status.Should().Be(EmailStatus.Failed);
    }
}

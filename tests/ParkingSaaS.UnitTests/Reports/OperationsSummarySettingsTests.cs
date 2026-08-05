using FluentAssertions;
using Microsoft.Extensions.Options;
using ParkingSaaS.Application.Common.Options;
using ParkingSaaS.Application.Reports;
using ParkingSaaS.Contracts.Reports;
using ParkingSaaS.Domain.Tenants;
using ParkingSaaS.UnitTests.Common;

namespace ParkingSaaS.UnitTests.Reports;

public sealed class OperationsSummarySettingsTests
{
    [Fact]
    public async Task Update_settings_changes_only_the_current_tenant()
    {
        var now = new DateTimeOffset(2026, 8, 5, 14, 0, 0, TimeSpan.Zero);
        var context = new MutableTenantContext();
        await using var db = InMemoryDb.Create(context);
        var current = TenantNamed("Current");
        var other = TenantNamed("Other");
        db.Tenants.AddRange(current, other);
        await db.SaveChangesAsync();
        context.ScopeTo(current.Id);
        var service = CreateService(db, context, now);

        var result = await service.UpdateSettingsAsync(
            new UpdateOperationsSummarySettingsRequest(false, 8),
            CancellationToken.None);

        result.Should().Be(new OperationsSummarySettingsResponse(false, 8));
        current.OperationsSummaryEnabled.Should().BeFalse();
        current.OperationsSummaryIntervalHours.Should().Be(8);
        current.OperationsSummaryLastRunAt.Should().Be(now);
        other.OperationsSummaryEnabled.Should().BeTrue();
        other.OperationsSummaryIntervalHours.Should().Be(3);
    }

    [Fact]
    public async Task Scheduled_sweep_runs_only_enabled_tenants_whose_interval_is_due()
    {
        var now = new DateTimeOffset(2026, 8, 5, 14, 0, 0, TimeSpan.Zero);
        var context = new MutableTenantContext();
        await using var db = InMemoryDb.Create(context);
        var due = TenantNamed("Due");
        due.ConfigureOperationsSummary(true, 6, now.AddHours(-7));
        due.MarkOperationsSummaryRun(now.AddHours(-7));
        var paused = TenantNamed("Paused");
        paused.ConfigureOperationsSummary(false, 1, now.AddHours(-12));
        var notDue = TenantNamed("Not due");
        notDue.ConfigureOperationsSummary(true, 12, now.AddHours(-2));
        db.Tenants.AddRange(due, paused, notDue);
        await db.SaveChangesAsync();
        var service = CreateService(db, context, now);

        var queued = await service.QueueScheduledEmailsAsync(CancellationToken.None);

        queued.Should().Be(0, "the test tenants have no administrators");
        due.OperationsSummaryLastRunAt.Should().Be(now);
        paused.OperationsSummaryLastRunAt.Should().Be(now.AddHours(-12));
        notDue.OperationsSummaryLastRunAt.Should().Be(now.AddHours(-2));
    }

    private static OperationsSummaryService CreateService(
        Infrastructure.Persistence.AppDbContext db,
        MutableTenantContext context,
        DateTimeOffset now)
        => new(
            db,
            new TestClock(now),
            null!,
            context,
            null!,
            Options.Create(new EmailOptions()));

    private static Tenant TenantNamed(string name)
        => new(name, $"{name.ToLowerInvariant().Replace(' ', '-')}-{Guid.NewGuid():N}", SubscriptionPlan.Free, "PHP", "Asia/Manila");
}

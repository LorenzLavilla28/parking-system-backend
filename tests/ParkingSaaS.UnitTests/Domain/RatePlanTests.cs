using FluentAssertions;
using ParkingSaaS.Domain.Common;
using ParkingSaaS.Domain.Pricing;
using ParkingSaaS.Domain.RatePlans;
using Xunit;

namespace ParkingSaaS.UnitTests.Domain;

public sealed class RatePlanTests
{
    [Fact]
    public void New_plan_starts_as_draft_and_activates()
    {
        var plan = new RatePlan(Guid.NewGuid(), "Standard", "Reusable weekday parking rates");
        plan.Status.Should().Be(RatePlanStatus.Draft);
        plan.Activate();
        plan.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Plan_requires_a_description()
    {
        var act = () => new RatePlan(Guid.NewGuid(), "X", "");
        act.Should().Throw<DomainException>().Which.Code.Should().Be("rateplan.description_required");
    }

    [Fact]
    public void Version_effective_window_is_evaluated_correctly()
    {
        var from = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var version = new RatePlanVersion(Guid.NewGuid(), Guid.NewGuid(), 1, from, "{}", Guid.NewGuid());

        version.IsEffectiveAt(from).Should().BeTrue();
        version.IsEffectiveAt(from.AddDays(5)).Should().BeTrue();

        version.Supersede(from.AddDays(10));
        version.IsEffectiveAt(from.AddDays(11)).Should().BeFalse();
        version.IsEffectiveAt(from.AddDays(9)).Should().BeTrue();
    }
}

public sealed class FeeQuoteTests
{
    private static FeeQuote Create(DateTimeOffset now)
        => new(Guid.NewGuid(), Guid.NewGuid(), "PHP", 70m, 0m, 70m, now, now.AddMinutes(10), "[]", Guid.NewGuid());

    [Fact]
    public void Active_quote_is_usable_before_expiry()
    {
        var now = DateTimeOffset.UtcNow;
        Create(now).IsActive(now).Should().BeTrue();
    }

    [Fact]
    public void Quote_is_inactive_after_expiry()
    {
        var now = DateTimeOffset.UtcNow;
        Create(now).IsActive(now.AddMinutes(11)).Should().BeFalse();
    }

    [Fact]
    public void Marking_used_transitions_from_active_only()
    {
        var quote = Create(DateTimeOffset.UtcNow);
        quote.MarkUsed();
        quote.Status.Should().Be(FeeQuoteStatus.Used);

        var act = () => quote.MarkUsed();
        act.Should().Throw<DomainException>().Which.Code.Should().Be("feequote.not_active");
    }

    [Fact]
    public void Negative_total_is_rejected()
    {
        var now = DateTimeOffset.UtcNow;
        var act = () => new FeeQuote(Guid.NewGuid(), Guid.NewGuid(), "PHP", 0m, 0m, -1m, now, now, "[]", null);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("feequote.total_negative");
    }
}

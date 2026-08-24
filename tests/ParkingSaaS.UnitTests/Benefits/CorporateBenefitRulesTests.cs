using FluentAssertions;
using ParkingSaaS.Domain.Benefits;
using Xunit;

namespace ParkingSaaS.UnitTests.Benefits;

public sealed class CorporateBenefitRulesTests
{
    [Fact]
    public void Produces_a_weekday_free_window_in_the_location_timezone()
    {
        var rules = new CorporateBenefitRules();
        var from = new DateTimeOffset(2026, 6, 24, 7, 30, 0, TimeSpan.FromHours(8));
        var until = from.AddHours(13.5);

        var intervals = rules.GetFreeIntervals(from, until, "Asia/Manila");

        intervals.Should().ContainSingle();
        intervals[0].From.Should().Be(new DateTimeOffset(2026, 6, 24, 8, 0, 0, TimeSpan.FromHours(8)));
        intervals[0].To.Should().Be(new DateTimeOffset(2026, 6, 24, 20, 0, 0, TimeSpan.FromHours(8)));
    }

    [Fact]
    public void Supports_cross_midnight_windows_without_freeing_the_wrong_day()
    {
        var rules = new CorporateBenefitRules
        {
            Windows = new() { new CorporateBenefitTimeWindow { Start = "22:00", End = "02:00" } },
            DaysOfWeek = new() { "Wednesday" }
        };
        var from = new DateTimeOffset(2026, 6, 25, 1, 0, 0, TimeSpan.FromHours(8)); // Thursday continuation of Wednesday's window

        var intervals = rules.GetFreeIntervals(from, from.AddHours(2), "Asia/Manila");

        intervals.Should().ContainSingle();
        intervals[0].From.Should().Be(from);
        intervals[0].To.Should().Be(new DateTimeOffset(2026, 6, 25, 2, 0, 0, TimeSpan.FromHours(8)));
    }

    [Fact]
    public void Excluded_holidays_do_not_receive_the_free_window()
    {
        var rules = new CorporateBenefitRules { ExcludeHolidays = true, Holidays = new() { "2026-06-24" } };
        var from = new DateTimeOffset(2026, 6, 24, 8, 0, 0, TimeSpan.FromHours(8));

        rules.GetFreeIntervals(from, from.AddHours(2), "Asia/Manila").Should().BeEmpty();
    }
}

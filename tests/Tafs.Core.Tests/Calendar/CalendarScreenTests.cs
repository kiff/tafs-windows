using Tafs.Core.Calendar;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Calendar;

[Part("calendar")]
public class CalendarScreenTests
{
    static readonly DateOnly Sunday = new(2026, 11, 15);
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    [Fact]
    public void It_opens_on_today_s_week_which_starts_on_Monday_and_its_strip_is_seven_days()
    {
        Assert.Equal(new CalendarState(CalendarMode.Week, Sunday), CalendarScreen.Opening(Sunday));
        Assert.Equal(6, CalendarScreen.WeekdayIndex(Sunday));
        Assert.Equal(new DateOnly(2026, 11, 9), CalendarScreen.WeekStart(Sunday));
        var strip = CalendarScreen.StripDays(new(CalendarMode.Week, Sunday));
        Assert.Equal(7, strip.Count);
        Assert.Equal((new DateOnly(2026, 11, 9), Sunday), (strip[0], strip[6]));
    }

    [Fact]
    public void The_day_strip_is_the_month_s_six_weeks_and_month_has_none()
    {
        var strip = CalendarScreen.StripDays(new(CalendarMode.Day, Sunday));
        Assert.Equal(42, strip.Count);
        Assert.Equal(new DateOnly(2026, 10, 26), strip[0]);
        Assert.Contains(Sunday, strip);
        Assert.Empty(CalendarScreen.StripDays(new(CalendarMode.Month, Sunday)));
    }

    [Theory]
    [InlineData(728, 749, CalendarLayout.Compact)]
    [InlineData(1600, 1000, CalendarLayout.Wide)]
    [InlineData(699, 600, CalendarLayout.Compact)]
    [InlineData(700, 600, CalendarLayout.Wide)]
    [InlineData(800, 800, CalendarLayout.Compact)]
    [InlineData(900, 599, CalendarLayout.Compact)]
    public void Wide_from_700_wide_wider_than_high_and_600_high(double width, double height, CalendarLayout layout) =>
        Assert.Equal(layout, CalendarScreen.LayoutOf(width, height));

    [Theory]
    [InlineData(CalendarMode.Day, 1, 2026, 11, 16)]
    [InlineData(CalendarMode.Week, -1, 2026, 11, 8)]
    [InlineData(CalendarMode.Month, 1, 2026, 12, 15)]
    public void The_arrows_step_a_day_a_week_or_a_month(CalendarMode mode, int by, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), CalendarScreen.Step(new(mode, Sunday), by).Selected);

    [Fact]
    public void A_month_step_lands_on_the_month_s_last_day_when_it_has_fewer() =>
        Assert.Equal(new DateOnly(2027, 2, 28), CalendarScreen.Step(new(CalendarMode.Month, new DateOnly(2027, 1, 31)), 1).Selected);

    [Fact]
    public void The_titles_are_the_reference_s()
    {
        Assert.Equal("Sun 15 November 2026", CalendarScreen.Title(En, new(CalendarMode.Day, Sunday)));
        Assert.Equal("9 – 15 November 2026", CalendarScreen.Title(En, new(CalendarMode.Week, Sunday)));
        Assert.Equal("November 2026", CalendarScreen.Title(En, new(CalendarMode.Month, Sunday)));
        Assert.Equal("Sun 15 November", CalendarScreen.DayHeading(En, Sunday));
    }

    [Fact]
    public void A_week_across_months_or_years_names_both()
    {
        Assert.Equal("30 November – 6 December 2026", CalendarScreen.Title(En, new(CalendarMode.Week, new DateOnly(2026, 12, 2))));
        Assert.Equal("28 December 2026 – 3 January 2027", CalendarScreen.Title(En, new(CalendarMode.Week, new DateOnly(2026, 12, 31))));
    }

    [Fact]
    public void The_modes_and_arrows_are_named()
    {
        Assert.Equal(new[] { "Day", "Week", "Month" }, CalendarScreen.Modes(En).Select(m => m.Title));
        Assert.Equal(("Previous day", "Next day"), CalendarScreen.Arrows(En, CalendarMode.Day));
        Assert.Equal(("Previous month", "Next month"), CalendarScreen.Arrows(En, CalendarMode.Month));
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_line_is_there_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        var all = Enum.GetValues<CalendarMode>().SelectMany(m => new[] { CalendarScreen.Title(w, new(m, Sunday)), CalendarScreen.Arrows(w, m).Previous, CalendarScreen.Arrows(w, m).Next })
            .Concat(CalendarScreen.Modes(w).Select(m => m.Title))
            .Concat(CalendarScreen.Week(Sunday).Select(d => CalendarScreen.WeekdayName(w, d)))
            .Concat(Enumerable.Range(1, 12).Select(m => CalendarScreen.Title(w, new(CalendarMode.Month, new DateOnly(2026, m, 1)))))
            .Concat([CalendarScreen.Heading(w), CalendarScreen.Today(w), CalendarScreen.DayHeading(w, Sunday),
                CalendarScreen.Title(w, new(CalendarMode.Week, new DateOnly(2026, 12, 31)))]);
        Assert.All(all, line => { Assert.False(string.IsNullOrWhiteSpace(line)); Assert.DoesNotContain("{", line); });
    }
}

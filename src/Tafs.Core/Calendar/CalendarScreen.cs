using System.Globalization;
using Tafs.Core.Words;

namespace Tafs.Core.Calendar;

/// <summary>The calendar's three views (kiff/tafs#534). Week is the one it opens on.</summary>
public enum CalendarMode { Day, Week, Month }

/// <summary>Wide or compact by the calendar's own pane, as the iPad's CalendarLayout.of.</summary>
public enum CalendarLayout { Compact, Wide }

/// <summary>What the calendar shows: its view and the selected date.</summary>
public sealed record CalendarState(CalendarMode Mode, DateOnly Selected);

/// <summary>The iPad's calendar (CalendarView, CalendarMath): Gregorian days, weeks from Monday.</summary>
public static class CalendarScreen
{
    public static CalendarState Opening(DateOnly today) => new(CalendarMode.Week, today);

    public static CalendarLayout LayoutOf(double width, double height) =>
        width >= 700 && width > height && height >= 600 ? CalendarLayout.Wide : CalendarLayout.Compact;

    /// <summary>0 for Monday to 6 for Sunday.</summary>
    public static int WeekdayIndex(DateOnly day) => ((int)day.DayOfWeek + 6) % 7;

    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-WeekdayIndex(day));

    public static IReadOnlyList<DateOnly> Week(DateOnly day) => Enumerable.Range(0, 7).Select(i => WeekStart(day).AddDays(i)).ToList();

    /// <summary>Six weeks from the Monday on or before the month's first day: every week with a day of the month.</summary>
    public static IReadOnlyList<DateOnly> MonthGrid(DateOnly day)
    {
        var start = WeekStart(new DateOnly(day.Year, day.Month, 1));
        return Enumerable.Range(0, 42).Select(start.AddDays).ToList();
    }

    /// <summary>The strip: in Week, exactly the seven days its title names; in Day, the month's weeks, which scroll. Month has none.</summary>
    public static IReadOnlyList<DateOnly> StripDays(CalendarState s) => s.Mode switch
    {
        CalendarMode.Week => Week(s.Selected),
        CalendarMode.Day => MonthGrid(s.Selected),
        _ => [],
    };

    /// <summary>The previous or next day, week or month.</summary>
    public static CalendarState Step(CalendarState s, int by) => s with
    {
        Selected = s.Mode switch
        {
            CalendarMode.Day => s.Selected.AddDays(by),
            CalendarMode.Week => s.Selected.AddDays(7 * by),
            _ => s.Selected.AddMonths(by),
        },
    };

    public static string Heading(WordBook w) => w.Get("calendar.heading");
    public static string Today(WordBook w) => w.Get("calendar.today");

    public static IReadOnlyList<(CalendarMode Mode, string Title)> Modes(WordBook w) =>
        [(CalendarMode.Day, w.Get("calendar.mode.day")), (CalendarMode.Week, w.Get("calendar.mode.week")), (CalendarMode.Month, w.Get("calendar.mode.month"))];

    /// <summary>The arrows' names: "Previous day", "Next week"…</summary>
    public static (string Previous, string Next) Arrows(WordBook w, CalendarMode mode)
    {
        var unit = mode switch { CalendarMode.Day => "day", CalendarMode.Week => "week", _ => "month" };
        return (w.Get($"calendar.previous_{unit}"), w.Get($"calendar.next_{unit}"));
    }

    static readonly string[] Weekdays = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];
    static readonly string[] Months = ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"];

    public static string WeekdayName(WordBook w, DateOnly day) => w.Get("calendar.weekday." + Weekdays[WeekdayIndex(day)]);
    static string MonthName(WordBook w, int month) => w.Get("calendar.month_name." + Months[month - 1]);

    /// <summary>The view's title in the language's order: "Sun 15 November 2026", "9 – 15 November 2026", "November 2026".</summary>
    public static string Title(WordBook w, CalendarState s)
    {
        var d = s.Selected;
        string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        switch (s.Mode)
        {
            case CalendarMode.Day:
                return w.Get("calendar.title.day", ("weekday", WeekdayName(w, d)), ("day", N(d.Day)), ("month", MonthName(w, d.Month)), ("year", N(d.Year)));
            case CalendarMode.Week:
                var (a, b) = (Week(d)[0], Week(d)[6]);
                if (a.Year != b.Year)
                    return w.Get("calendar.title.week_across_years", ("from_day", N(a.Day)), ("from_month", MonthName(w, a.Month)), ("from_year", N(a.Year)),
                        ("to_day", N(b.Day)), ("to_month", MonthName(w, b.Month)), ("to_year", N(b.Year)));
                if (a.Month != b.Month)
                    return w.Get("calendar.title.week_across_months", ("from_day", N(a.Day)), ("from_month", MonthName(w, a.Month)),
                        ("to_day", N(b.Day)), ("to_month", MonthName(w, b.Month)), ("year", N(b.Year)));
                return w.Get("calendar.title.week", ("from_day", N(a.Day)), ("to_day", N(b.Day)), ("month", MonthName(w, b.Month)), ("year", N(b.Year)));
            default:
                return w.Get("calendar.title.month", ("month", w.Get("calendar.month_title." + Months[d.Month - 1])), ("year", N(d.Year)));
        }
    }

    /// <summary>A day as a heading: "Sun 15 November" (the eyebrow draws it in capitals).</summary>
    public static string DayHeading(WordBook w, DateOnly day) =>
        w.Get("calendar.day_heading", ("weekday", WeekdayName(w, day)), ("day", day.Day.ToString(CultureInfo.InvariantCulture)), ("month", MonthName(w, day.Month)));


    /// <summary>The hour the hours open at: 08:00, as the iPad's with nothing scheduled earlier.</summary>
    public const int FirstHour = 8;
}

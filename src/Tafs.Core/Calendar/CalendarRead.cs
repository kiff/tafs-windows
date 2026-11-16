using System.Globalization;
using System.Text.Json;
using Tafs.Core.Taf;

namespace Tafs.Core.Calendar;

/// <summary>
/// One occurrence of a taf's work to come (a live schedule's firing, a follow-up it set itself, a run's next try), as tafs
/// cloud places it: its taf (tafs cloud's id), name and colour, its kind, the instant, and its day, time and minute in the
/// account's zone, so nothing here converts a zone; what it is in tafs cloud's words, and how many firings it stands for.
/// </summary>
public sealed record CalendarEvent(string Taf, string Name, int Color, string Kind, string At, DateOnly Day, string Time, int Minute,
    string Words, int Count);

/// <summary>
/// The tafs' scheduled work still to come on a range of dates (<c>GET /v1/calendar?from=&amp;to=</c>, kiff/tafs#534), as the
/// iPad's <c>TafsCloud.CalendarRead</c>: its events, tafs cloud's line for the range, and whether some were left out or the
/// account is halted.
/// </summary>
public sealed record CalendarRead(DateOnly From, DateOnly To, IReadOnlyList<CalendarEvent> Events, string Words, bool More, bool Halted)
{
    public static string Path(DateOnly from, DateOnly to) =>
        $"/v1/calendar?from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    /// <summary>tafs cloud's answer: an event this app can't read is left out, never the read. None for one that isn't an object.</summary>
    public static CalendarRead? Read(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) return null;
        var events = Lenient.Array(a, "events").Select(Event).OfType<CalendarEvent>().ToList();
        return new(Day(Lenient.Text(a, "from")) ?? default, Day(Lenient.Text(a, "to")) ?? default, events,
            Lenient.Text(a, "words") ?? "", Lenient.Bool(a, "more"), Lenient.Bool(a, "halted"));
    }

    static CalendarEvent? Event(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || Lenient.Text(e, "taf") is not { } taf || Lenient.Text(e, "at") is not { } at
            || Day(Lenient.Text(e, "day")) is not { } day || Lenient.Text(e, "time") is not { } time || Lenient.Int(e, "minute") is not { } minute)
            return null;
        return new(taf, Lenient.Text(e, "name") ?? "", Lenient.Int(e, "color") ?? 0, Lenient.Text(e, "kind") ?? "schedule", at, day, time, minute,
            Lenient.Text(e, "words") ?? "", Math.Max(1, Lenient.Int(e, "count") ?? 1));
    }

    static DateOnly? Day(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}

/// <summary>One hour of a day: each taf with work in it once, with how many times, in the order of its first.</summary>
public sealed record CalendarSlot(string Taf, string Name, int Color, int Count, string First);

/// <summary>The iPad's <c>CalendarMath</c> over tafs cloud's events: what each view draws, placed by tafs cloud's day and minute.</summary>
public static class CalendarWork
{
    /// <summary>The dates one read covers: the selected month's grid, which holds its day and its week too, so one read draws every view.</summary>
    public static (DateOnly From, DateOnly To) Range(DateOnly selected)
    {
        var grid = CalendarScreen.MonthGrid(selected);
        return (grid[0], grid[^1]);
    }

    /// <summary>The read when it answers the selected date's range; none while that range is read, so no view says "nothing" from another month.</summary>
    public static CalendarRead? Current(CalendarRead? read, DateOnly selected) =>
        read is not null && (read.From, read.To) == Range(selected) ? read : null;

    /// <summary>
    /// Whether an answer still applies when it lands: the range asked is still the one being read, by the same session.
    /// Another range asked since, or another account signed in meanwhile, drops it: one account's work is never drawn for another.
    /// </summary>
    public static bool Lands((DateOnly, DateOnly) asked, (DateOnly, DateOnly)? reading, object askedBy, object? sessionNow) =>
        reading == asked && ReferenceEquals(askedBy, sessionNow);

    /// <summary>The dates a view shows: the day, its week, or the month's own days.</summary>
    public static IReadOnlyList<DateOnly> Shown(CalendarState s) => s.Mode switch
    {
        CalendarMode.Day => [s.Selected],
        CalendarMode.Week => CalendarScreen.Week(s.Selected),
        _ => CalendarScreen.MonthGrid(s.Selected).Where(d => d.Month == s.Selected.Month).ToList(),
    };

    /// <summary>A day's events in time order (then by name, then instant).</summary>
    public static IReadOnlyList<CalendarEvent> On(IReadOnlyList<CalendarEvent> events, DateOnly day) =>
        events.Where(e => e.Day == day).OrderBy(e => e.Minute).ThenBy(e => e.Name, StringComparer.Ordinal).ThenBy(e => e.At, StringComparer.Ordinal).ToList();

    /// <summary>The agenda: the dates' events by day, each in time order; days with none left out.</summary>
    public static IReadOnlyList<(DateOnly Day, IReadOnlyList<CalendarEvent> Events)> Agenda(IReadOnlyList<CalendarEvent> events, IEnumerable<DateOnly> days) =>
        days.Select(d => (d, On(events, d))).Where(g => g.Item2.Count > 0).ToList();

    /// <summary>A day's work by hour (0 to 23), for the Day and Week time grids.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<CalendarSlot>> Slots(IReadOnlyList<CalendarEvent> events, DateOnly day)
    {
        var hours = new Dictionary<int, List<CalendarSlot>>();
        foreach (var e in On(events, day))
        {
            var hour = Math.Clamp(e.Minute / 60, 0, 23);
            if (!hours.TryGetValue(hour, out var row)) hours[hour] = row = [];
            var i = row.FindIndex(s => s.Taf == e.Taf);
            if (i >= 0) row[i] = row[i] with { Count = row[i].Count + e.Count };
            else row.Add(new CalendarSlot(e.Taf, e.Name, e.Color, e.Count, e.Time));
        }
        return hours.ToDictionary(h => h.Key, h => (IReadOnlyList<CalendarSlot>)h.Value);
    }

    /// <summary>The first hour with work among the dates, to scroll a time grid to; 8 when there's none.</summary>
    public static int FirstHour(IReadOnlyList<CalendarEvent> events, IEnumerable<DateOnly> days)
    {
        var set = days.ToHashSet();
        var hours = events.Where(e => set.Contains(e.Day)).Select(e => Math.Clamp(e.Minute / 60, 0, 23)).ToList();
        return hours.Count > 0 ? hours.Min() : CalendarScreen.FirstHour;
    }

    /// <summary>How many firings a day has, a folded event counting all it stands for.</summary>
    public static int Count(IReadOnlyList<CalendarEvent> events, DateOnly day) => events.Where(e => e.Day == day).Sum(e => e.Count);

    /// <summary>The colours of the tafs with work on a day, at most three, for its marks.</summary>
    public static IReadOnlyList<int> Marks(IReadOnlyList<CalendarEvent> events, DateOnly day) =>
        On(events, day).DistinctBy(e => e.Taf).Take(3).Select(e => e.Color).ToList();
}

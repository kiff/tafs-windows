using System.Text.Json;
using Tafs.Core.Calendar;

namespace Tafs.Core.Tests.Calendar;

/// <summary>The tafs' work to come (<c>GET /v1/calendar</c>), as the iPad's CalendarRead and CalendarMath place it.</summary>
[Part("calendar")]
public class CalendarReadTests
{
    static readonly DateOnly Mon = new(2026, 11, 16);

    const string Answer = """
        {"time_zone": "Europe/Paris", "from": "2026-10-26", "to": "2026-12-06", "words": "5 to come", "more": false, "halted": false,
         "events": [
           {"taf": "t1", "name": "Morning inbox", "color": 3, "kind": "schedule", "at": "2026-11-17T07:00:00Z", "day": "2026-11-17", "time": "08:00", "minute": 480, "words": "Every weekday at 08:00"},
           {"taf": "t2", "name": "Refunds", "color": 5, "kind": "schedule", "at": "2026-11-17T07:30:00Z", "day": "2026-11-17", "time": "08:30", "minute": 510, "words": "Every 30 min", "count": 4},
           {"taf": "t1", "name": "Morning inbox", "color": 3, "kind": "follow_up", "at": "2026-11-17T07:45:00Z", "day": "2026-11-17", "time": "08:45", "minute": 525, "words": "Check the refund"},
           {"taf": "t3", "name": "Chargebacks", "color": 1, "kind": "retry", "at": "2026-11-19T13:00:00Z", "day": "2026-11-19", "time": "14:00", "minute": 840, "words": "Try 2 of 3"},
           {"taf": "t9", "day": "2026-11-20"},
           "not an event"
         ]}
        """;

    static CalendarRead Read() => CalendarRead.Read(JsonDocument.Parse(Answer).RootElement)!;

    [Fact]
    public void It_reads_the_events_and_leaves_out_one_it_can_t()
    {
        var read = Read();
        Assert.Equal((new DateOnly(2026, 10, 26), new DateOnly(2026, 12, 6), "5 to come", false, false), (read.From, read.To, read.Words, read.More, read.Halted));
        Assert.Equal(4, read.Events.Count);
        Assert.Equal(4, read.Events[1].Count);
        Assert.Equal(1, read.Events[0].Count);
        Assert.Null(CalendarRead.Read(JsonDocument.Parse("[]").RootElement));
        Assert.Equal("/v1/calendar?from=2026-10-26&to=2026-12-06", CalendarRead.Path(new(2026, 10, 26), new(2026, 12, 6)));
    }

    /// <summary>One read covers the selected month's six weeks; a read for another range is none, so nothing says "nothing" early.</summary>
    [Fact]
    public void One_read_covers_the_month_grid_and_only_its_own_range_counts()
    {
        Assert.Equal((new DateOnly(2026, 10, 26), new DateOnly(2026, 12, 6)), CalendarWork.Range(Mon));
        var read = Read();
        Assert.Same(read, CalendarWork.Current(read, Mon));
        Assert.NotNull(CalendarWork.Current(Read(), new DateOnly(2026, 11, 30)));
        Assert.Null(CalendarWork.Current(Read(), new DateOnly(2026, 12, 15)));
        Assert.Null(CalendarWork.Current(null, Mon));
    }

    /// <summary>An answer landing after another range was asked, or after another account signed in, is dropped.</summary>
    [Fact]
    public void A_late_answer_lands_only_for_its_range_and_session()
    {
        var range = CalendarWork.Range(Mon);
        object session = new(), next = new();
        Assert.True(CalendarWork.Lands(range, range, session, session));
        Assert.False(CalendarWork.Lands(range, CalendarWork.Range(Mon.AddMonths(1)), session, session));
        Assert.False(CalendarWork.Lands(range, range, session, next));
        Assert.False(CalendarWork.Lands(range, range, session, null));
    }

    [Fact]
    public void A_day_s_work_by_hour_counts_each_taf_once_with_its_firings()
    {
        var events = Read().Events;
        var tue = new DateOnly(2026, 11, 17);
        Assert.Equal(["08:00", "08:30", "08:45"], CalendarWork.On(events, tue).Select(e => e.Time));
        var eight = CalendarWork.Slots(events, tue)[8];
        Assert.Equal([new CalendarSlot("t1", "Morning inbox", 3, 2, "08:00"), new CalendarSlot("t2", "Refunds", 5, 4, "08:30")], eight);
        Assert.Equal(6, CalendarWork.Count(events, tue));
        Assert.Equal([3, 5], CalendarWork.Marks(events, tue));
        Assert.Equal(8, CalendarWork.FirstHour(events, CalendarScreen.Week(Mon)));
        Assert.Equal(CalendarScreen.FirstHour, CalendarWork.FirstHour(events, [Mon]));
    }

    [Fact]
    public void The_agenda_is_the_shown_days_with_work()
    {
        var agenda = CalendarWork.Agenda(Read().Events, CalendarWork.Shown(new CalendarState(CalendarMode.Week, Mon)));
        Assert.Equal([new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 19)], agenda.Select(g => g.Day));
        Assert.Equal(30, CalendarWork.Shown(new CalendarState(CalendarMode.Month, Mon)).Count);
        Assert.Equal([Mon], CalendarWork.Shown(new CalendarState(CalendarMode.Day, Mon)));
    }
}

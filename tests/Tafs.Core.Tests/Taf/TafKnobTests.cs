using System.Text;
using System.Text.Json;
using Tafs.Core.Home;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>A taf's dial (<c>GET /v1/tafs/{id}/knob</c>), as Setup's Starts when and Results go to read it, and tafs' own places.</summary>
[Part("record")]
public class TafKnobTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    /// <summary>A dial as kiff/tafs docs/api.md describes it: a schedule with its size, an app event to approve, a follow-up, places.</summary>
    const string Dial = """
        {
          "version": 3,
          "pending": {"version": 4, "changes": 1},
          "draft": {"version": 4, "words": ["Every hour instead of every day at 09:00"], "wakes": [], "when": [], "results": []},
          "wakes": [
            {"id": "w1", "kind": "schedule", "label": "09:00", "words": "Every day at 09:00", "trigger": {"kind": "schedule", "form": "daily", "at": "09:00"},
             "size": {"label": "How often", "index": 1, "choices": [{"label": "Every hour", "setting": {"wakes": [], "when": {"busy": "skip"}}}, {"label": "Every day", "setting": {"wakes": []}}]}},
            {"id": "w2", "kind": "app_event", "label": "HubSpot · new deal", "words": "When a deal is made", "on": false, "ok": "approve", "grant": "grt_1",
             "approval": {"state": "needed", "grant": "grt_1", "event": "deal.created", "config": {}}},
            {"id": "w3", "kind": "follow_up", "label": "Check the refund", "words": "", "follow_up": "fu_1", "expires": "2026-11-18T09:00:00Z"},
            {"id": "w4", "kind": "run_now", "label": "Run now", "words": ""}
          ],
          "results": [
            {"id": "r1", "place": "door_reply", "label": "Reply", "words": "", "on": true},
            {"id": "r2", "place": "app", "label": "Phones", "words": "", "on": false},
            {"id": "r3", "place": "action", "label": "Notion · Support log", "words": "A line per answer", "on": true, "grant": "grt_2",
             "size": {"label": "Notion", "index": 1, "choices": [{"label": "Off", "setting": {}}, {"label": "On", "setting": {}}]}}
          ],
          "setting": {"wakes": [], "when": {"busy": "latest"}, "results": ["door_reply", {"place": "action", "grant": "grt_2"}]}
        }
        """;

    static TafKnob Knob(string json = Dial) => TafKnob.Read(JsonDocument.Parse(json).RootElement)!;

    [Fact]
    public void It_reads_each_ring_by_kind()
    {
        var knob = Knob();
        Assert.True(knob.CanPause);
        Assert.Equal((3, new KnobPending(4, 1)), (knob.Version, knob.Pending));
        Assert.Equal(["Every hour instead of every day at 09:00"], knob.DraftWords);
        var schedule = Assert.Single(knob.Schedules);
        Assert.Equal(("Every day at 09:00", "How often · Every day"), (schedule.Line, schedule.Size!.Shown));
        var ev = Assert.Single(knob.AppEvents);
        Assert.Equal((false, "approve", "needed", "deal.created"), (ev.On, ev.Ok, ev.Approval!.State, ev.Approval.Event));
        var follow = Assert.Single(knob.FollowUps);
        Assert.Equal(("Check the refund", "fu_1", new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero)), (follow.Line, follow.FollowUp, follow.Expires));
        Assert.Equal(["r3"], knob.AppPlaces.Select(p => p.Id));
        Assert.Equal("latest", knob.Busy);
        Assert.Equal(["door_reply", "action"], knob.SettingPlaces!);
    }

    [Fact]
    public void A_taf_that_works_from_its_chat_can_t_pause_and_an_empty_dial_is_one()
    {
        var knob = Knob("""{"can_pause": false}""");
        Assert.False(knob.CanPause);
        Assert.Empty(knob.Wakes);
        Assert.Null(knob.Pending);
        Assert.Null(knob.Busy);
    }

    /// <summary>As strict as the iPad's decoder: a stop with no id, or a field of the wrong type, is no dial.</summary>
    [Theory]
    [InlineData("""{"wakes": [{"label": "09:00"}]}""")]
    [InlineData("""{"wakes": [{"id": "w1", "on": "yes"}]}""")]
    [InlineData("""{"wakes": [{"id": "w1", "words": 3}]}""")]
    [InlineData("""{"results": {"id": "r1"}}""")]
    [InlineData("""{"pending": {"changes": 1}}""")]
    [InlineData("""{"wakes": [{"id": "w1", "size": {"label": "x"}}]}""")]
    [InlineData("""[]""")]
    public void One_it_can_t_read_is_no_dial(string json) => Assert.Null(TafKnob.Read(JsonDocument.Parse(json).RootElement));

    [Fact]
    public void Its_routes_escape_the_ids()
    {
        Assert.Equal("/v1/tafs/t%2F1/knob", TafKnob.Path("t/1"));
        Assert.Equal("/v1/tafs/t1/follow-ups/fu%2F1", TafKnob.FollowUpPath("t1", "fu/1"));
        Assert.Equal(("/v1/tafs/t1/pause", "/v1/tafs/t1/resume"), (TafKnob.PausePath("t1", true), TafKnob.PausePath("t1", false)));
        Assert.Equal("/v1/tafs/t1/results", TafKnob.ResultsPath("t1"));
    }

    /// <summary>tafs' own places on: the live taf's, never a waiting version's; the dial's only when nothing waits.</summary>
    [Fact]
    public void Its_own_places_are_the_live_taf_s()
    {
        Assert.Equal(["door_reply", "owner_email"], ResultPlaces.Live(["owner_email", "door_reply", "elsewhere"], Knob()));
        // A version waits: the dial isn't the live one, so nothing is known without Home's.
        Assert.Null(ResultPlaces.Live(null, Knob()));
        var live = Knob(Dial.Replace("\"pending\": {\"version\": 4, \"changes\": 1},", ""));
        Assert.Equal(["door_reply"], ResultPlaces.Live(null, live));
        Assert.Null(ResultPlaces.Live(null, null));
    }

    [Fact]
    public void Switching_one_sends_the_live_ones_with_it_changed_and_never_none()
    {
        Assert.Equal(["door_reply", "app"], ResultPlaces.Switched(["door_reply"], "app", true));
        Assert.Equal(["owner_email"], ResultPlaces.Switched(["door_reply", "owner_email"], "door_reply", false));
        Assert.Null(ResultPlaces.Switched(["app"], "app", false));
        Assert.Equal("""{"places":["door_reply","app"]}""", Encoding.UTF8.GetString(ResultPlaces.Body(["door_reply", "app"])));
        // What the live taf has now, as tafs cloud answers; none when it doesn't say.
        Assert.Equal(["door_reply", "app"], ResultPlaces.Answered(JsonDocument.Parse("""{"taf": {"results": ["door_reply", "app"]}}""").RootElement));
        Assert.Null(ResultPlaces.Answered(JsonDocument.Parse("""{"ok": true}""").RootElement));
    }

    /// <summary>The last one on can't be turned off; none switches unknown, busy, or for someone who doesn't manage the taf.</summary>
    [Fact]
    public void The_last_place_on_stays_on()
    {
        var shown = ResultPlaces.Shown(En, ["door_reply"], "ana@example.com", manages: true, busy: false);
        Assert.Equal(["door_reply", "app", "owner_email"], shown.Select(p => p.Place));
        Assert.Equal([true, false, false], shown.Select(p => p.On));
        Assert.Equal([false, true, true], shown.Select(p => p.CanSwitch));
        Assert.Contains("ana@example.com", shown[2].Line);
        Assert.Equal("Its answer to your email; not when you wrote the message", ResultPlaces.Shown(En, null, null, true, false)[2].Line);
        Assert.All(ResultPlaces.Shown(En, null, null, true, false), p => Assert.False(p.CanSwitch));
        Assert.All(ResultPlaces.Shown(En, ["door_reply", "app"], null, true, busy: true), p => Assert.False(p.CanSwitch));
        Assert.All(ResultPlaces.Shown(En, ["door_reply", "app"], null, manages: false, false), p => Assert.False(p.CanSwitch));
    }

    /// <summary>Home's taf says when it starts and where its results go.</summary>
    [Fact]
    public void Home_says_when_it_starts_and_where_results_go()
    {
        var home = HomeList.Read(En, Tafs.Core.Contracts.ContractFile.Answer(SharedFiles.Contract("home.json")), TimeZoneInfo.Utc);
        var starts = home.Tafs[0].Starts!;
        Assert.Equal(("Next run Fri 12:30", false), (starts.NextRunWords, starts.Unscheduled));
        Assert.Equal(["door_reply"], starts.Results!);
    }

    /// <summary>Coach's dial card shows a draft only for the version it names (agentgabo's read on #244).</summary>
    [Fact]
    public void A_dial_card_shows_the_draft_only_for_the_version_it_names()
    {
        var knob = new TafKnob(true, 3, null, [], [], ["Every Monday at 9"], null, null, DraftVersion: 4);
        Assert.True(knob.ShowsDraft(4));
        Assert.False(knob.ShowsDraft(5));
        Assert.False(knob.ShowsDraft(null));
        Assert.False((knob with { DraftVersion = null }).ShowsDraft(null));
        Assert.True(knob.IsLive(3));
        Assert.False(knob.IsLive(4));
        Assert.False(knob.IsLive(null));
    }
}

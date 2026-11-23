using System.Text;
using System.Text.Json;
using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>Install this version and the dial's changes, as the iPad's useVersion, changeKnob and adjustKnob.</summary>
[Part("record")]
public class TafSwitchTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static CloudRead Answer(string json) => CloudRead.Of(JsonDocument.Parse(json).RootElement);

    sealed class Calls
    {
        public List<string> Said { get; } = [];
        public Dictionary<string, CloudRead> Cloud { get; } = [];
        public string Draft { get; set; } = """{"id":"d_3","agent_id":"rae-k3f9","status":"draft","version":"v7"}""";
        public (int, string) Confirmed { get; set; } = (200, """{"id":"d_3","agent_id":"rae-k3f9","status":"confirmed","version":"v7"}""");
        public Exception? SignFails { get; set; }
        public StepConfirmed Here { get; set; } = StepConfirmed.Yes;

        public SwitchCalls Make() => new(new TrialCalls(
            (method, path, body) =>
            {
                Said.Add($"{method} {path}");
                return Task.FromResult(Cloud.TryGetValue($"{method} {path}", out var a) ? a : Answer("""{"ok": true}"""));
            },
            path => { Said.Add("KIFF GET " + path); return Task.FromResult(Answer(Draft)); },
            (path, body) =>
            {
                Said.Add("sign " + path);
                return SignFails is { } e ? Task.FromException<(string, string)>(e) : Task.FromResult(("KIFF-Passkey-Assertion", "a"));
            },
            (path, body, signature) => { Said.Add("KIFF POST " + path); return Task.FromResult(Confirmed); },
            path => Task.FromResult((200, "{}")), () => true, e => e.Message),
            prompt => { Said.Add("hello " + prompt); return Task.FromResult(Here); });
    }

    /// <summary>The Card the owner read: d_3 at v7.</summary>
    static readonly Tafs.Core.Approvals.AgentDraft Shown = new("d_3", "rae-k3f9", "draft", "v7");

    static Calls WithCard()
    {
        var calls = new Calls();
        calls.Cloud["POST /v1/tafs/t1/versions/3/switch"] = Answer("""{"switching": {"n": 3, "draft": "d_3", "at": "2026-11-17T01:00:00Z"}}""");
        return calls;
    }

    [Fact]
    public async Task A_version_with_a_Card_is_begun_its_Card_confirmed_then_made_live()
    {
        var calls = WithCard();
        Assert.Equal(new SwitchResult(true), await TafSwitch.InstallAsync(En, "t1", "Rae", 3, Shown, calls.Make()));
        Assert.Equal(["POST /v1/tafs/t1/versions/3/switch", "KIFF GET /v1/me/agent-drafts/d_3", "sign /v1/me/agent-drafts/d_3/confirm",
            "KIFF POST /v1/me/agent-drafts/d_3/confirm", "POST /v1/tafs/t1/versions/3/live"], calls.Said);
        Assert.Equal("""{"redraft":false,"stop_blocking":true}""", Encoding.UTF8.GetString(TafSwitch.Body(false, true)));
    }

    [Fact]
    public async Task A_version_with_no_Card_asks_Windows_Hello_only()
    {
        var calls = new Calls();
        calls.Cloud["POST /v1/tafs/t1/versions/2/switch"] = Answer("""{"switching": {"n": 2, "at": "2026-11-17T01:00:00Z"}}""");
        Assert.True((await TafSwitch.InstallAsync(En, "t1", "Rae", 2, null, calls.Make())).Live);
        Assert.Equal(["POST /v1/tafs/t1/versions/2/switch", "hello Use version 2 of Rae", "POST /v1/tafs/t1/versions/2/live"], calls.Said);

        var closed = new Calls { Here = StepConfirmed.Cancelled };
        closed.Cloud["POST /v1/tafs/t1/versions/2/switch"] = Answer("""{"switching": {"n": 2, "at": "2026-11-17T01:00:00Z"}}""");
        Assert.Equal(new SwitchResult(false), await TafSwitch.InstallAsync(En, "t1", "Rae", 2, null, closed.Make()));
        Assert.Equal("DELETE /v1/tafs/t1/switching", closed.Said[^1]);
    }

    [Fact]
    public async Task A_Card_changed_since_it_was_shown_is_shown_again_and_the_switch_dropped()
    {
        var calls = WithCard();
        calls.Draft = """{"id":"d_3","agent_id":"rae-k3f9","status":"draft","version":"v8"}""";
        var result = await TafSwitch.InstallAsync(En, "t1", "Rae", 3, Shown, calls.Make());
        Assert.Equal("v8", result.Changed!.Version);
        Assert.DoesNotContain(calls.Said, s => s.StartsWith("sign", StringComparison.Ordinal));
        Assert.Equal("DELETE /v1/tafs/t1/switching", calls.Said[^1]);

        // Begun again, another draft at the same version: not the one read, so never signed.
        var other = new Calls();
        other.Cloud["POST /v1/tafs/t1/versions/3/switch"] = Answer("""{"switching": {"n": 3, "draft": "d_9", "at": "2026-11-17T01:00:00Z"}}""");
        other.Draft = """{"id":"d_9","agent_id":"rae-k3f9","status":"draft","version":"v7"}""";
        Assert.Equal("d_9", (await TafSwitch.InstallAsync(En, "t1", "Rae", 3, Shown, other.Make())).Changed!.Id);
        Assert.DoesNotContain(other.Said, s => s.StartsWith("sign", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_Card_KIFF_says_moved_on_is_drafted_again()
    {
        var calls = WithCard();
        calls.Confirmed = (409, """{"error": "a Card changed", "code": "card_revised"}""");
        var result = await TafSwitch.InstallAsync(En, "t1", "Rae", 3, Shown, calls.Make());
        Assert.True(result.Stale);
        Assert.Equal("Version 3's Card is out of date: a Card changed since it was drafted. Draft it again, then read it before you approve.", result.Why);
        Assert.DoesNotContain(calls.Said, s => s.EndsWith("/live", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_run_in_the_way_says_so_in_tafs_cloud_s_words()
    {
        var calls = new Calls();
        calls.Cloud["POST /v1/tafs/t1/versions/3/switch"] = CloudRead.Refused(En, 409, """{"error": "busy", "words": "Rae is waiting on you for a refund."}""");
        var begun = await TafSwitch.BeginAsync(En, "t1", 3, false, false, calls.Make().Calls);
        Assert.Equal(new SwitchBegun(null, "Rae is waiting on you for a refund.", Blocked: true), begun);
    }

    [Fact]
    public async Task A_switch_that_stopped_a_waiting_task_names_it_in_tafs_cloud_s_words()
    {
        var calls = new Calls();
        calls.Cloud["POST /v1/tafs/t1/versions/3/switch"] = Answer("""
            {"switching": {"n": 3, "draft": null, "at": "2026-11-23T04:20:00Z"},
             "stopped": [{"run": "r1", "session": "s1"}, {"run": "r2"}, 7],
             "words": " The task that was waiting on your answer was stopped, and its message didn't run. "}
            """);
        var begun = await TafSwitch.BeginAsync(En, "t1", 3, false, true, calls.Make().Calls);
        Assert.Equal(3, begun.Switch!.N);
        Assert.Equal([new StoppedRun("r1", "s1")], begun.Stopped);
        Assert.Equal("The task that was waiting on your answer was stopped, and its message didn't run.", begun.StoppedWords);
        // Nothing waiting stopped: no words, even if some are sent.
        calls.Cloud["POST /v1/tafs/t1/versions/3/switch"] = Answer("""{"switching": {"n": 3}, "words": "x"}""");
        var quiet = await TafSwitch.BeginAsync(En, "t1", 3, false, true, calls.Make().Calls);
        Assert.Empty(quiet.Stopped);
        Assert.Null(quiet.StoppedWords);
    }

    [Fact]
    public void Retry_sends_the_owner_s_last_message_of_a_stopped_session_and_nothing_when_none_is_loaded()
    {
        var at = new DateTimeOffset(2026, 11, 23, 4, 0, 0, TimeSpan.Zero);
        Tafs.Core.Coach.ChatMessage M(Tafs.Core.Coach.ChatFrom from, string text, string session, int s) => new(at.AddSeconds(s), from, false, text, session);
        var messages = new[]
        {
            M(Tafs.Core.Coach.ChatFrom.Owner, "Refund Ana", "s1", 1),
            M(Tafs.Core.Coach.ChatFrom.Taf, "On it", "s1", 2),
            M(Tafs.Core.Coach.ChatFrom.Owner, "And Bo too", "s1", 3),
            M(Tafs.Core.Coach.ChatFrom.Owner, "Something else", "s2", 4),
        };
        Assert.Equal("And Bo too", TafSwitch.RetryText(messages, [new StoppedRun("r1", "s1")]));
        // None of the stopped session's messages loaded: no Retry, so nothing that ran is sent twice.
        Assert.Null(TafSwitch.RetryText(messages, [new StoppedRun("r9", "s9")]));
        Assert.Null(TafSwitch.RetryText([], [new StoppedRun("r1", "s1")]));
    }

    const string Dial = """
        {"version": 3, "pending": {"version": 4, "changes": 1}, "shown": 4, "draft": {"version": 4, "words": ["Every 2 hours"]},
         "wakes": [{"id": "w1", "kind": "schedule", "label": "Every hour",
                    "size": {"label": "How often", "index": 1, "choices": [
                      {"label": "Every 30 min", "setting": {"wakes": [{"type": "schedule", "every": "30m"}], "busy": "skip", "results": ["app"]}},
                      {"label": "Every hour", "setting": {"wakes": [{"type": "schedule", "every": "1h"}], "busy": "skip", "results": ["app"]}},
                      {"label": "Every 2 hours", "setting": {"wakes": [{"type": "schedule", "every": "2h"}], "busy": "skip", "results": ["app"]}}]}},
                   {"id": "e1", "kind": "app_event", "label": "New refund", "trigger": {"event": "refund.created", "type": "app_event"}}],
         "setting": {"wakes": [{"type": "schedule", "every": "1h"}, {"type": "app_event", "event": "refund.created"}], "when": {"busy": "skip"},
                     "results": ["app", {"place": "notion", "page": "p1"}], "why": "refunds"}}
        """;

    [Fact]
    public void A_change_to_the_dial_sends_the_live_setting_whole_with_one_thing_changed()
    {
        var k = TafKnob.Read(JsonDocument.Parse(Dial).RootElement)!;
        Assert.True(k.AcceptsChoices());
        Assert.False(k.AcceptsChoices(waitingFor: 5));
        Assert.Equal("""{"wakes":[{"type":"schedule","every":"1h"},{"type":"app_event","event":"refund.created"}],"when":{"busy":"latest"},"results":["app",{"place":"notion","page":"p1"}],"why":"refunds"}""",
            Encoding.UTF8.GetString(k.WithBusy("latest")!));
        Assert.Null(k.WithBusy("skip"));
        Assert.Equal("""{"wakes":[{"type":"schedule","every":"1h"}],"when":{"busy":"skip"},"results":["app",{"place":"notion","page":"p1"}],"why":"refunds"}""",
            Encoding.UTF8.GetString(k.WithoutWake(k.AppEvents.Single().Trigger!.Value)!));
        var size = k.Schedules.Single().Size!;
        Assert.Equal("Every 2 hours", size.Moving(1)!.Label);
        Assert.Equal("Every 30 min", size.Moving(-1)!.Label);
        Assert.Null(size.Moving(2));
        Assert.Equal("""{"wakes":[{"type":"schedule","every":"2h"}],"when":{"busy":"skip"},"results":["app"]}""",
            Encoding.UTF8.GetString(TafKnob.SettingBody(size.Moving(1)!.Setting)));
        var made = KnobChange.Read(JsonDocument.Parse("""{"version": 4, "words": ["Every 2 hours"]}""").RootElement)!;
        Assert.Equal((4, "Every 2 hours"), (made.Version, made.Words.Single()));
    }
}

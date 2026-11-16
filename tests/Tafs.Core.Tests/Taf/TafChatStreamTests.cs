using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>A taf's answer as it comes, as kiff/tafs docs/chat.md's stream and the iPad's LiveAnswer.</summary>
[Part("record")]
public class TafChatStreamTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static IReadOnlyList<StreamEvent> Events(string stream) => ServerSentEvents.Parse(stream.Split('\n')).ToList();

    /// <summary>Events end at a blank line; comments are skipped; data lines join; data that isn't JSON reads as none.</summary>
    [Fact]
    public void Server_sent_events_read_as_tafs_cloud_writes_them()
    {
        var events = Events("""
            : keep-alive
            event: session
            data: {"session":"s1",
            data: "run":"r1"}

            event: text
            data: {"text":"Hel"}

            event: broken
            data: not json
            """);
        Assert.Equal(["session", "text", "broken"], events.Select(e => e.Name));
        Assert.Equal("s1", events[0].Data!.Value.GetProperty("session").GetString());
        Assert.Null(events[2].Data);
    }

    /// <summary>
    /// A run's stream, start to end: the chat and run noted, the owner's message by id, the steps with their words and how
    /// they ended, the words as they come, and the live line (state words with "…", Waiting for you, none at done).
    /// </summary>
    [Fact]
    public void The_live_answer_follows_the_stream()
    {
        var live = LiveAnswer.Start;
        Assert.Equal("Thinking…", live.Line(En));
        foreach (var e in Events("""
            event: session
            data: {"session":"s1","new_session":false,"run":"r1","message":{"id":"m7","at":"2026-11-16T10:00:00Z"}}

            event: thinking
            data: {"text":"I should list the payments"}

            event: activity
            data: {"tool":"kiff__stripe_list","step":"Asking KIFF: List payments","call_id":"c1"}

            """)) live = live.Apply(e);
        Assert.Equal(("s1", "r1", "m7"), (live.Session, live.Run, live.MessageId));
        // Before tafs cloud's state: the step at work stands in. The thinking text is never kept.
        Assert.Equal("Asking KIFF: List payments…", live.Line(En));
        Assert.Equal("", live.Text);

        foreach (var e in Events("""
            event: step
            data: {"call_id":"c1","status":"error","failed":true,"why":"No such payment intent"}

            event: state
            data: {"state":"working","words":"Checking the refunds"}

            event: text
            data: {"text":"Two refunds "}

            event: text
            data: {"text":"are due."}

            """)) live = live.Apply(e);
        Assert.Equal(new LiveStep("c1", "Asking KIFF: List payments", "error", "No such payment intent"), Assert.Single(live.Steps));
        Assert.Equal("Checking the refunds…", live.Line(En));
        Assert.Equal("Two refunds are due.", live.Text);

        live = live.Apply(Events("event: state\ndata: {\"state\":\"waiting_for_you\"}\n")[0]);
        Assert.Equal("Waiting for you", live.Line(En));
        live = live.Apply(Events("event: end\ndata: {\"status\":\"ERROR\",\"stop\":\"error\",\"code\":\"ai_unavailable\",\"words\":\"The AI isn't answering. Nothing was used.\"}\n")[0]);
        Assert.True(live.Ended);
        Assert.Null(live.Line(En));
        Assert.Equal(("ERROR", "The AI isn't answering. Nothing was used."), (live.EndStatus, live.EndWords));
    }

    /// <summary>A broken stream ends the answer (the thread has the rest); taken asks for the thread; unknown events change nothing.</summary>
    [Fact]
    public void A_broken_stream_ends_and_unknown_events_change_nothing()
    {
        var live = LiveAnswer.Start.Apply(Events("event: taken\ndata: {}\n")[0]);
        Assert.True(live.Taken);
        Assert.Equal(live, live.Apply(Events("event: component\ndata: {\"component\":{}}\n")[0]));
        Assert.Equal(live, live.Apply(Events("event: text\ndata: {\"words\":\"no text field\"}\n")[0]));
        var broken = live.Apply(Events("event: error\ndata: {\"error\":\"gone\"}\n")[0]);
        Assert.True(broken is { Ended: true, Broken: true });
        Assert.Equal("/v1/tafs/taf%2F1/messages/stream", TafChatStream.Path("taf/1"));
    }
}

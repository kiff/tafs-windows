using System.Text.Json;
using Tafs.Core.Coach;

namespace Tafs.Core.Tests.Coach;

/// <summary>A taf's cards for Coach (its thread's ask_coach components), as the iPad's AskCoachCard.</summary>
[Part("coach")]
public class AskCoachCardTests
{
    [Fact]
    public void A_taf_s_thread_carries_its_cards_for_Coach_and_leaves_out_one_it_cant_read()
    {
        var thread = CoachThread.Read(JsonDocument.Parse("""
            {"messages": [{"at": "2026-11-17T10:48:00Z", "from": "owner", "text": "Which emails have no answer?", "session": "s1"}],
             "components": [
               {"at": "2026-11-17T10:48:06Z", "session": "s1", "run": "r1",
                "component": {"type": "ask_coach", "id": "ac1", "text": " I want Morning inbox to read my Gmail. "}},
               {"at": "2026-11-17T10:49:00Z", "session": "s1", "run": "r1", "component": {"type": "ask_coach", "id": "ac2", "text": "  "}},
               {"session": "s1", "run": "r1", "component": {"type": "ask_coach", "id": "ac3", "text": "no time"}},
               {"at": "2026-11-17T10:49:00Z", "session": "s1", "run": "r1", "component": {"type": "notice", "id": "n1", "text": "not one"}}]}
            """).RootElement)!;
        var card = Assert.Single(thread.AskCoach);
        Assert.Equal(new AskCoachCard("ac1", DateTimeOffset.Parse("2026-11-17T10:48:06Z"), "s1", "I want Morning inbox to read my Gmail."), card);
        Assert.Single(thread.Messages);
        Assert.Empty(CoachThread.Read(JsonDocument.Parse("""{"messages": []}""").RootElement)!.AskCoach);
    }
}

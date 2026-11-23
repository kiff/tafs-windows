using System.Text;
using System.Text.Json;
using Tafs.Core.Coach;

namespace Tafs.Core.Tests.Coach;

/// <summary>A taf's loop card ("Still reading": Read on, Stop here), as Android's answerTafCard and the iPad's LoopCard.</summary>
[Part("coach")]
public class LoopCardTests
{
    const string Loop = """{"at":"2026-10-14T09:02:00Z","session":"sesn_9","run":"r1","component":{"type":"confirm","id":"tafs-loop-1","loop":{"tool":"list_invoices","pages":5},"title":"Still reading","text":"It has read 5 pages of the same list.","options":[{"value":"tafs_loop_continue","label":"Read on"},{"value":"tafs_loop_summarise","label":"Stop here"}]}}""";
    const string Ask = """{"at":"2026-10-14T09:03:00Z","session":"sesn_9","component":{"type":"ask_coach","id":"ac_1","text":"Raise the daily limit to 30"}}""";
    const string Plain = """{"at":"2026-10-14T09:04:00Z","session":"sesn_9","component":{"type":"confirm","id":"c_1","title":"Go on?","options":[{"value":"yes","label":"Yes"}]}}""";

    static CoachThread Thread(params string[] components) => CoachThread.Read(JsonDocument.Parse(
        """{"messages":[{"at":"2026-10-14T09:01:00Z","from":"owner","kind":"message","text":"List my invoices","session":"sesn_9"}],"components":[""" +
        string.Join(",", components) + "]}").RootElement)!;

    [Fact]
    public void Only_a_confirm_that_carries_loop_is_a_loop_card_and_ask_coach_reads_as_before()
    {
        var thread = Thread(Loop, Ask, Plain);
        var card = Assert.Single(thread.LoopCards);
        Assert.Equal(("tafs-loop-1", "sesn_9", "Still reading", "It has read 5 pages of the same list.", (string?)null),
            (card.Id, card.Session, card.Title, card.Text, card.Answered));
        Assert.Equal(["Read on", "Stop here"], card.Options.Select(o => o.Label));
        Assert.Single(thread.AskCoach);
    }

    [Theory]
    [InlineData("\"id\":\"tafs-loop-1\"", "\"id\":\"other-1\"")]
    [InlineData("\"loop\":{\"tool\":\"list_invoices\",\"pages\":5},", "")]
    [InlineData("\"type\":\"confirm\"", "\"type\":\"questions\"")]
    [InlineData("\"options\":[{\"value\":\"tafs_loop_continue\",\"label\":\"Read on\"},{\"value\":\"tafs_loop_summarise\",\"label\":\"Stop here\"}]", "\"options\":[{\"value\":\"\",\"label\":\"Read on\"}]")]
    public void Anything_else_is_left_out_never_the_thread(string from, string to) =>
        Assert.Empty(Thread(Loop.Replace(from, to)).LoopCards);

    [Fact]
    public void An_answered_card_says_how_and_offers_nothing_to_send()
    {
        var answered = Thread(Loop.Replace("\"component\":", "\"answer\":{\"state\":\"answered\",\"words\":\" Read on \"},\"component\":")).LoopCards[0];
        Assert.Equal("Read on", answered.Answered);
        Assert.Null(answered.Answer("taf_1", "tafs_loop_continue"));
    }

    /// <summary>The option's value to the taf's own route, with the card's chat; a value it doesn't offer sends nothing.</summary>
    [Fact]
    public void The_answer_is_the_option_s_value_on_the_taf_s_route()
    {
        var card = Thread(Loop).LoopCards[0];
        var (path, body) = card.Answer("taf/1", "tafs_loop_summarise")!.Value;
        Assert.Equal("/v1/tafs/taf%2F1/components/tafs-loop-1/answer", path);
        Assert.Equal("""{"answer":"tafs_loop_summarise","session":"sesn_9"}""", Encoding.UTF8.GetString(body));
        Assert.Null(card.Answer("taf/1", "something_else"));
        var noChat = card with { Session = "" };
        Assert.Equal("""{"answer":"tafs_loop_continue"}""", Encoding.UTF8.GetString(noChat.Answer("t", "tafs_loop_continue")!.Value.Body));
    }
}

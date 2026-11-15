using Tafs.Core.Coach;
using Tafs.Core.Contracts;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>
/// Coach's reads against tafs cloud's contracts at the pin (setup-coach.json, coach-chats.json, coach-conversation.json),
/// read as the iPad's loadSetupThread, setupThread(decoding:), SetupThread.items and its cards do.
/// </summary>
[Part("coach")]
public class CoachContractTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static Task<CloudRead> Contract(string path) => Task.FromResult(CloudRead.Of(ContractFile.Answer(SharedFiles.Contract(
        path == "/v1/setup" ? "setup-coach.json" : path.EndsWith("/chats") ? "coach-chats.json" : "coach-conversation.json"))));

    static async Task<CoachThread> Thread() => (await CoachChat.LoadAsync(En, Contract)).Thread!;

    /// <summary>Coach's id from /v1/setup, its newest chat from the list, then that conversation.</summary>
    [Fact]
    public async Task Coach_s_newest_conversation_reads_from_the_contracts()
    {
        var asked = new List<string>();
        var (thread, error, coach) = await CoachChat.LoadAsync(En, path => { asked.Add(path); return Contract(path); });

        Assert.Equal(new[] { "/v1/setup", "/v1/tafs/taf-c0ac4000000000000000/chats", "/v1/tafs/taf-c0ac4000000000000000/conversation/sess1" }, asked);
        Assert.Equal(("taf-c0ac4000000000000000", (string?)null), (coach, error));
        Assert.Equal(4, thread!.Messages.Count);
        Assert.Equal("sess1", thread.Working);
        Assert.Equal("Rae", Assert.Single(thread.Lines).Taf);
    }

    /// <summary>
    /// The thread in order, each card where the iPad places it: the questions card after Coach's words with it, the person's
    /// own words answering it as theirs (it was written past, so it's open, as on the iPad), the approval, the result.
    /// </summary>
    [Fact]
    public async Task The_thread_places_its_cards_as_the_iPad_does()
    {
        var thread = await Thread();

        string What(ThreadItem i) => i.Card?.Ref ?? i.Message!.Text.Split(' ')[0];
        Assert.Equal(new[] { "Set", "Shippo's", "sess1#limits", "Up", "sess1#connect-stripe", "sess1#done", "Refunds" }, thread.Items.Select(What));
        var written = thread.Messages.Single(m => m.Text.StartsWith("Up to"));
        Assert.Null(thread.AnswerTo("limits"));
        Assert.Equal("limits", written.Answers);
        Assert.Equal("approved", thread.AnswerTo("connect-stripe")!.Text);
        Assert.Contains(thread.Drawn("questions", "notice", "result"), i => i.Message == written);
        Assert.DoesNotContain(thread.Drawn("questions", "notice", "result"), i => i.Card?.Card.Kind == "approval");
    }

    /// <summary>The result tafs cloud answered itself: set up, its taf, its tools in words.</summary>
    [Fact]
    public async Task The_result_is_set_up_as_tafs_cloud_answered_it()
    {
        var thread = await Thread();
        var done = thread.Shown.Single(c => c.Card.Id == "done");

        Assert.Equal(new CardTaf("refunds-x", "Refunds"), done.Card.Taf);
        Assert.Equal(HandOverState.SetUp, thread.HandOver(done.Ref, listed: false).State);
        Assert.Equal("list requests k3f9", CoachChat.ToolWords(Assert.Single(done.Card.Tools!)));
        Assert.True(thread.ReadHomeFor(["rae"], new HashSet<string>()));
    }
}

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

    /// <summary>
    /// Only Coach's conversation carries this PC's planque proof: the newest or one by its session, never its chats list,
    /// /v1/setup or a taf's own chat.
    /// </summary>
    [Fact]
    public async Task Only_Coach_s_conversation_read_carries_the_proof()
    {
        var asked = new List<string>();
        await CoachChat.LoadAsync(En, path => { asked.Add(path); return Contract(path); });
        Assert.Equal([false, false, true], asked.Select(CoachChat.SignsRead));
        Assert.True(CoachChat.SignsRead(CoachChat.ConversationPath("taf-c", null)));
        Assert.True(CoachChat.SignsRead("/v1/tafs/taf-c/conversation/s1?can=approve"));
        Assert.False(CoachChat.SignsRead("/v1/tafs/taf-c/conversation/"));
        Assert.False(CoachChat.SignsRead("/v1/tafs/taf-c/messages"));
        Assert.False(CoachChat.SignsRead("/v1/tafs/taf-c/conversation/s1/more"));
        Assert.Equal("/v1/tafs/taf%2Fc/conversation/s%2F1", CoachChat.ConversationPath("taf/c", "s/1"));
    }

    /// <summary>
    /// What tafs cloud sends on a proven read: the contract's conversation with its approval marked <c>wider</c>, and a sign-in
    /// for the same app beside it, marked too. Both cards carry tafs cloud's words, as sent (the Mac's #134 test).
    /// </summary>
    [Fact]
    public void Wider_reaches_both_an_approval_and_a_sign_in_card()
    {
        const string words = "The sign-in for Stripe can't be narrowed to this job: it asks for more access than the taf uses.";
        var answer = System.Text.Json.Nodes.JsonNode.Parse(ContractFile.Answer(SharedFiles.Contract("coach-conversation.json")).GetRawText())!;
        void Mark(System.Text.Json.Nodes.JsonArray list, Func<System.Text.Json.Nodes.JsonNode, System.Text.Json.Nodes.JsonNode?> card)
        {
            var approval = list.First(e => card(e!)?["component"]?["type"]?.GetValue<string>() == "approval")!;
            var shown = card(approval)!;
            shown["component"]!["wider"] = new System.Text.Json.Nodes.JsonObject { ["words"] = words };
            // Waiting, as a card the person still answers: no answer yet.
            shown.AsObject().Remove("answer");
            var signIn = shown.DeepClone();
            signIn["component"]!["type"] = "sign_in";
            signIn["component"]!["id"] = "sign-in-stripe";
            signIn["key"] = "sess1#sign-in-stripe";
            signIn["seq"] = 7;
            list.Add(card(approval) == approval ? signIn : new System.Text.Json.Nodes.JsonObject { ["type"] = "card", ["card"] = signIn });
        }
        Mark(answer["components"]!.AsArray(), c => c);
        Mark(answer["items"]!.AsArray(), c => c["card"]);

        var thread = CoachThread.Read(System.Text.Json.JsonDocument.Parse(answer.ToJsonString()).RootElement)!;
        var marked = thread.Shown.Where(s => s.Card.Kind is "approval" or "sign_in").ToList();
        Assert.Equal(["approval", "sign_in"], marked.Select(s => s.Card.Kind));
        Assert.All(marked, s => Assert.Equal(words, s.Card.Wider));
    }

    /// <summary>
    /// tafs cloud's own <c>coach-wider.json</c>: the conversation as a proven read answers it, its approval
    /// marked <c>wider</c>. Windows reads tafs cloud's words on the card, as sent.
    /// </summary>
    [Fact]
    public void Tafs_cloud_s_wider_contract_reaches_the_approval_card()
    {
        var thread = CoachThread.Read(ContractFile.Answer(SharedFiles.Contract("coach-wider.json")))!;
        var approval = Assert.Single(thread.Shown, s => s.Card.Kind == "approval");
        Assert.Equal("Composio connects your Google Sheets account. Google Sheets may ask for broader access than this job needs, but your taf can only use the tools allowed on its Card. tafs enforces those limits and blocks every other tool call.",
            approval.Card.Wider);
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Approvals;
using Tafs.Core.Coach;
using Tafs.Core.Contracts;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>Coach's tools still to give (P8), as the iPad's CoachTools and giveCoachTools, for a Coach that already exists.</summary>
[Part("coach")]
public class CoachToolsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    /// <summary>A Coach that exists, its tools to give as the contract's pair (the first start's requests, as tafs cloud sends them now).</summary>
    static JsonElement Setup(string state = "to_give", Action<JsonObject>? change = null)
    {
        var answer = JsonNode.Parse(ContractFile.Answer(SharedFiles.Contract("setup-start.json")).GetRawText())!.AsObject();
        var tools = answer["start"]!.DeepClone().AsObject();
        tools["state"] = state;
        change?.Invoke(tools);
        var setup = new JsonObject { ["setup"] = new JsonObject { ["id"] = "taf_coach" }, ["tools"] = tools };
        return JsonDocument.Parse(setup.ToJsonString()).RootElement.Clone();
    }

    sealed class Stubs
    {
        public readonly List<string> Calls = [];
        public Queue<JsonElement> Reads = new();
        public Exception? HelloFails;
        public (int, string) Batch = (200, "{}"), Draft = (201, """{"id": "drf_2", "status": "confirmed"}""");

        public StartCalls Make() => new(
            () => { Calls.Add("read"); return Task.FromResult(CloudRead.Of(Reads.Dequeue())); },
            (id, message) => Task.CompletedTask,
            (body, at) => { Calls.Add("sign draft"); return HelloFails is { } e ? Task.FromException<(string, string)>(e) : Task.FromResult(("KIFF-Passkey-Assertion", "d")); },
            (connect, draft, at) =>
            {
                Calls.Add($"sign together {connect.Id} {draft.Id}");
                return HelloFails is { } e ? Task.FromException<(string, string, string)>(e) : Task.FromResult(("KIFF-Passkey-Assertion", "b", "d"));
            },
            () => Task.FromResult(new EnrolResult(EnrolOutcome.Enrolled)),
            (body, signature) => { Calls.Add($"batch {signature.Value}"); return Task.FromResult(Batch); },
            (body, signature) => { Calls.Add($"draft {signature.Value}"); return Task.FromResult(Draft); },
            body => Task.FromResult(CloudRead.Of(JsonDocument.Parse("{}").RootElement)),
            () => "2026-11-17T03:00:00.000Z", () => true) { Mcp = Mcp };

        /// <summary>Coach's own control server at the contracts' tafs cloud: the one a batch may connect.</summary>
        public string Mcp = "https://tafs.test/v1/mcp";
    }

    /// <summary>The step as the person read it.</summary>
    static CoachTools Shown => CoachTools.Read(Setup())!;

    [Fact]
    public void Coach_s_tools_read_with_their_pair_and_the_step_s_words()
    {
        var tools = CoachTools.Read(Setup())!;
        Assert.True(tools.ToGive);
        Assert.NotNull(tools.Together);
        Assert.Equal(("connect", "draft"), (tools.Together!.Connect!.Id, tools.Together.Draft!.Id));
        Assert.Equal("Coach's tools are waiting for you · Give them", tools.Row(En));
        Assert.Equal(("Give Coach its tools", "Not now"), (tools.Step!.Give, tools.Step.NotNow));
        Assert.NotEmpty(tools.Step.Groups);
        Assert.Null(CoachTools.Read(Setup("given"))!.Row(En));
        Assert.Null(CoachTools.Read(JsonDocument.Parse("""{"setup": {"id": "c"}}""").RootElement));
        // tafs cloud with no row of its words: the app's own.
        Assert.Equal("Give Coach its tools", CoachTools.Read(Setup(change: t => t["coach_tools_step"]!.AsObject().Remove("row")))!.Row(En));
    }

    /// <summary>
    /// The batch went, yet tafs cloud still asks for the very same batch: nothing was connected. Give says so, in tafs cloud's
    /// words when it sends them, else "Coach's tools weren't given.", never that KIFF didn't answer; as the iPad's and Android's.
    /// </summary>
    [Fact]
    public async Task A_batch_that_connects_nothing_says_so()
    {
        var stubs = new Stubs();
        stubs.Reads.Enqueue(Setup());
        stubs.Reads.Enqueue(Setup());
        Assert.Equal(new ToolsGiven(false, "Coach's tools weren't given."), await CoachToolsGive.GiveAsync(En, stubs.Make(), Shown));
        Assert.Contains("batch b", stubs.Calls);

        var worded = new Stubs();
        worded.Reads.Enqueue(Setup());
        worded.Reads.Enqueue(Setup(change: t => t["coach_tools_step"]!.AsObject()["not_connected"] = " Nothing was connected. "));
        Assert.Equal("Nothing was connected.", (await CoachToolsGive.GiveAsync(En, worded.Make(), Shown)).Why);

        // Still to give, but asking for another batch: not this case; tafs cloud's read is what it says.
        var other = new Stubs();
        other.Reads.Enqueue(Setup());
        other.Reads.Enqueue(Setup(change: t => t["requests"]![0]!["body"]!.AsObject()["another"] = true));
        Assert.Equal(En.Get("coach.tools_unknown"), (await CoachToolsGive.GiveAsync(En, other.Make(), Shown)).Why);
    }

    [Fact]
    public async Task The_pair_goes_under_one_Windows_Hello_the_batch_first()
    {
        var stubs = new Stubs();
        stubs.Reads.Enqueue(Setup());
        stubs.Reads.Enqueue(Setup("given"));
        Assert.Equal(new ToolsGiven(true), await CoachToolsGive.GiveAsync(En, stubs.Make(), Shown));
        Assert.Equal(["read", "sign together connect draft", "batch b", "draft d", "read"], stubs.Calls);
    }

    [Fact]
    public async Task Taken_says_which_name_and_Windows_Hello_closed_sends_nothing()
    {
        var taken = new Stubs();
        taken.Reads.Enqueue(Setup("taken", t => t["taken"] = "list_tafs"));
        Assert.Equal("Coach can't have its tools yet: another tool in KIFF is named list_tafs. Rename or remove it in KIFF.",
            (await CoachToolsGive.GiveAsync(En, taken.Make(), Shown)).Why);
        Assert.Equal(["read"], taken.Calls);

        var closed = new Stubs { HelloFails = new OperationCanceledException() };
        closed.Reads.Enqueue(Setup());
        Assert.Equal(new ToolsGiven(false), await CoachToolsGive.GiveAsync(En, closed.Make(), Shown));
        Assert.DoesNotContain(closed.Calls, c => c.StartsWith("batch", StringComparison.Ordinal));

        var refused = new Stubs { Batch = (403, """{"error": "passkey_required"}""") };
        refused.Reads.Enqueue(Setup());
        Assert.Equal("Coach's tools weren't given. passkey_required", (await CoachToolsGive.GiveAsync(En, refused.Make(), Shown)).Why);
        Assert.DoesNotContain(refused.Calls, c => c.StartsWith("draft", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pair_rebuilt_since_the_step_was_read_signs_nothing_and_comes_back_to_show()
    {
        // tafs cloud added a tool to the batch between the step and the tap.
        static void MoreTools(JsonObject t)
        {
            foreach (var r in t["requests"]!.AsArray())
                if (r!["id"]!.GetValue<string>() == "connect") r["body"]!.AsObject()["more"] = "list_everything";
        }
        var stubs = new Stubs();
        stubs.Reads.Enqueue(Setup(change: MoreTools));
        var given = await CoachToolsGive.GiveAsync(En, stubs.Make(), Shown);
        Assert.False(given.Given);
        Assert.Equal("Setup changed this request: see below", given.Why);
        Assert.NotNull(given.Changed);
        Assert.False(Shown.SignsAsShown(given.Changed!));
        Assert.Equal(["read"], stubs.Calls);

        // Other words for Coach's Cards than the step showed: the same.
        var words = new Stubs();
        words.Reads.Enqueue(Setup(change: t =>
        {
            foreach (var r in t["requests"]!.AsArray())
                if (r!["id"]!.GetValue<string>() == "draft") r["lines"] = new JsonArray("Spend up to 1,000,000 a day");
        }));
        Assert.NotNull((await CoachToolsGive.GiveAsync(En, words.Make(), Shown)).Changed);
        Assert.Equal(["read"], words.Calls);

        // What was shown, read again as it was: it signs.
        Assert.True(Shown.SignsAsShown(CoachTools.Read(Setup())!));
    }

    /// <summary>
    /// Coach takes no typing until its tools are approved (the iPhone's composerLocked): while the tools themselves are still to
    /// give, the box is one locked line in tafs cloud's words, else the app's "Coach's tools"; never blank, never open.
    /// </summary>
    [Fact]
    public void While_its_tools_are_to_give_Coach_s_box_is_one_locked_line()
    {
        var tools = CoachTools.Read(Setup())!;
        Assert.True(tools.LocksBox);
        Assert.Equal("Coach's tools are waiting for you · Give them", tools.LockedLine(En));
        foreach (var blank in new[] { "", "  " })
            Assert.Equal("Coach's tools", CoachTools.Read(Setup(change: t => t["coach_tools_step"]!["row"] = blank))!.LockedLine(En));
        Assert.Equal("Coach's tools", CoachTools.Read(Setup(change: t => t["coach_tools_step"]!.AsObject().Remove("row")))!.LockedLine(En));
        // Given: the box opens.
        Assert.False(CoachTools.Read(Setup("given"))!.LocksBox);
        // From tafs cloud's own Coach read (setup-coach.json), whose tools are to give with a batch: locked too.
        Assert.True(CoachTools.Read(ContractFile.Answer(SharedFiles.Contract("setup-coach.json")))!.LocksBox);
    }

    [Fact]
    public void Only_Cards_for_newer_tools_missing_leaves_typing_open()
    {
        // No batch to connect, alone or in the pair: only Cards to approve.
        var cardsOnly = CoachTools.Read(Setup(change: t =>
        {
            var requests = t["requests"]!.AsArray();
            var draft = requests.Single(r => r!["id"]!.GetValue<string>() == "draft")!.DeepClone();
            t.Remove("requests");
            t["card"] = draft;
        }))!;
        Assert.True(cardsOnly.ToGive);
        Assert.Null(cardsOnly.Connect);
        Assert.Null(cardsOnly.Together);
        Assert.False(cardsOnly.LocksBox);
    }

    [Fact]
    public void The_Card_step_s_locked_line_falls_back_to_the_app_s_own_never_blank()
    {
        var words = FirstStart.Read(ContractFile.Answer(SharedFiles.Contract("setup-start.json")))!.Words!;
        Assert.Equal("Approve this first, then you can write to Coach.", words.LockedLine(En));
        Assert.Equal("Approve Coach's tools first, then you can write.", (words with { Locked = "" }).LockedLine(En));
    }

    /// <summary>
    /// A later approval of Coach's Cards, as the iPhone's draftSigns: the tools tafs cloud marks in this draft must be exactly
    /// what it signs; a mismatch signs nothing, before Windows Hello.
    /// </summary>
    [Fact]
    public async Task A_later_approval_whose_list_isn_t_its_draft_signs_nothing()
    {
        void Tamper(JsonObject t) =>
            t["requests"]!.AsArray().Single(r => r!["id"]!.GetValue<string>() == "draft")!["body"]!["cards"]![0]!["aggregates"]![0]!["limit"] = 999;
        var tampered = CoachTools.Read(Setup(change: Tamper))!;
        Assert.False(tampered.DraftSigns);
        var stubs = new Stubs();
        stubs.Reads.Enqueue(Setup(change: Tamper));
        var given = await CoachToolsGive.GiveAsync(En, stubs.Make(), tampered);
        Assert.Equal("What you're asked to approve changed since it was shown. Read it again, then approve.", given.Why);
        Assert.Equal(["read"], stubs.Calls);

        // A batch that connects another server: nothing signed either.
        var elsewhere = new Stubs { Mcp = "https://elsewhere.example/v1/mcp" };
        elsewhere.Reads.Enqueue(Setup());
        Assert.False((await CoachToolsGive.GiveAsync(En, elsewhere.Make(), Shown)).Given);
        Assert.Equal(["read"], elsewhere.Calls);

        // From a tafs cloud that doesn't mark the step's data, KIFF's own words are what's shown as signed.
        var unmarked = CoachTools.Read(Setup(change: t =>
        {
            foreach (var g in t["coach_tools_step"]!["groups"]!.AsArray())
                foreach (var tool in g!["tools"]!.AsArray()) tool!.AsObject().Remove("in_draft");
        }))!;
        Assert.False(unmarked.MarksDraft);
        Assert.True(unmarked.DraftSigns);
    }
}

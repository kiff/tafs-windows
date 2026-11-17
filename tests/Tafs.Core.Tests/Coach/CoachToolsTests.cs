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
            () => "2026-11-17T03:00:00.000Z", () => true);
    }

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

    [Fact]
    public async Task The_pair_goes_under_one_Windows_Hello_the_batch_first()
    {
        var stubs = new Stubs();
        stubs.Reads.Enqueue(Setup());
        stubs.Reads.Enqueue(Setup("given"));
        Assert.Equal(new ToolsGiven(true), await CoachToolsGive.GiveAsync(En, stubs.Make()));
        Assert.Equal(["read", "sign together connect draft", "batch b", "draft d", "read"], stubs.Calls);
    }

    [Fact]
    public async Task Taken_says_which_name_and_Windows_Hello_closed_sends_nothing()
    {
        var taken = new Stubs();
        taken.Reads.Enqueue(Setup("taken", t => t["taken"] = "list_tafs"));
        Assert.Equal("Coach can't have its tools yet: another tool in KIFF is named list_tafs. Rename or remove it in KIFF.",
            (await CoachToolsGive.GiveAsync(En, taken.Make())).Why);
        Assert.Equal(["read"], taken.Calls);

        var closed = new Stubs { HelloFails = new OperationCanceledException() };
        closed.Reads.Enqueue(Setup());
        Assert.Equal(new ToolsGiven(false), await CoachToolsGive.GiveAsync(En, closed.Make()));
        Assert.DoesNotContain(closed.Calls, c => c.StartsWith("batch", StringComparison.Ordinal));

        var refused = new Stubs { Batch = (403, """{"error": "passkey_required"}""") };
        refused.Reads.Enqueue(Setup());
        Assert.Equal("Coach's tools weren't given. passkey_required", (await CoachToolsGive.GiveAsync(En, refused.Make())).Why);
        Assert.DoesNotContain(refused.Calls, c => c.StartsWith("draft", StringComparison.Ordinal));
    }
}

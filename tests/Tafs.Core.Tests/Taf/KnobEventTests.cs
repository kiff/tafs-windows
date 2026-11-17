using System.Text;
using System.Text.Json;
using Tafs.Core.Connections;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>
/// An app event's filters, read and approved before planque subscribes (P4), as the iPad's AppEventApprovalSheet and
/// approveKnobEvent (AppEventApprovalTests).
/// </summary>
[Part("record")]
public class KnobEventTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    const string Win = "win_abcdefghijklmnopqrstuvwxyz";

    static string Dial(int version = 3, string state = "needed", string config = """{"pipeline": "sales", "amount_over": 500}""",
        string? subscription = null) => $$$"""
        {"version": {{{version}}},
         "wakes": [{"id": "w2", "kind": "app_event", "label": "HubSpot · new deal", "words": "When a deal is made", "on": false, "ok": "approve",
           "grant": "grt_1", "approval": {"state": "{{{state}}}", "grant": "grt_1", "event": "deal.created", "config": {{{config}}}{{{(subscription is null ? "" : $", \"subscription\": \"{subscription}\"")}}}}}],
         "results": [], "setting": {"wakes": []}}
        """;

    static TafKnob Knob(string json) => TafKnob.Read(JsonDocument.Parse(json).RootElement)!;
    static KnobItem Shown => Knob(Dial()).Wakes[0];

    static PlanqueGrant Grant(string status = "connected", string agent = "deals-k3", string? account = "acc_9") =>
        new("grt_1", status, "HubSpot", "d", new GrantRequest("hubspot", agent, []), []) { Account = account };

    sealed class Calls
    {
        public List<string> Said { get; } = [];
        public Queue<string> Reads { get; } = new();
        public List<PlanqueGrant> Grants { get; set; } = [Grant()];
        public bool HelloClosed { get; set; }
        public (int, string) Tap { get; set; } = (201, """{"id": "sub_7"}""");
        public string Recorded { get; set; } = Dial(state: "approved", subscription: "sub_7");

        public KnobEventCalls Make() => new(
            () => { Said.Add("read dial"); return Task.FromResult(CloudRead.Of(JsonDocument.Parse(Reads.Dequeue()).RootElement)); },
            new ConnectCalls(
                () => { Said.Add("grants"); return Task.FromResult<IReadOnlyList<PlanqueGrant>?>(Grants); },
                () => { Said.Add("read window"); return Task.FromResult<OpenWindow?>(new OpenWindow(Win, DateTimeOffset.UtcNow.AddMinutes(5), true, "device", null)); },
                () =>
                {
                    Said.Add("hello");
                    if (HelloClosed) throw new OperationCanceledException();
                    return Task.FromResult<(OpenWindow?, string?)>((new OpenWindow(Win, DateTimeOffset.UtcNow.AddMinutes(5), true, "device", null), null));
                },
                (window, path, body) => { Said.Add($"tap {path} {Encoding.UTF8.GetString(body)}"); return Task.FromResult(Tap); },
                (page, grant) => Task.FromResult<string?>(null), () => true, e => e.Message),
            body => { Said.Add($"record {Encoding.UTF8.GetString(body)}"); return Task.FromResult(CloudRead.Of(JsonDocument.Parse(Recorded).RootElement)); });
    }

    [Fact]
    public void The_sheet_shows_every_filter_sorted_and_indented()
    {
        var approval = Shown.Approval!;
        Assert.Equal("{\n  \"amount_over\": 500,\n  \"pipeline\": \"sales\"\n}", KnobEvent.Filters(approval).ReplaceLineEndings("\n"));
        Assert.Equal("{\n  \"url\": \"https://x.test/a\"\n}",
            KnobEvent.Filters(Knob(Dial(config: """{"url": "https://x.test/a"}""")).Wakes[0].Approval!).ReplaceLineEndings("\n"));
        Assert.True(KnobEvent.Asks(Shown));
        Assert.False(KnobEvent.Asks(Knob(Dial(state: "approved")).Wakes[0]));
    }

    [Fact]
    public async Task Approved_under_a_new_window_planque_subscribes_those_filters_then_tafs_cloud_records_it()
    {
        var calls = new Calls();
        calls.Reads.Enqueue(Dial());
        var result = await KnobEvent.ApproveAsync(En, "t_1", "deals-k3", 3, Shown, calls.Make(), kept: null);
        Assert.True(result.Approved, result.Why);
        Assert.Equal([
            "read dial", "grants",
            // Windows Hello each time, even with a window open.
            "hello",
            """tap /v1/accounts/t_1/grants/grt_1/triggers {"account":"acc_9","slug":"deal.created","config":{"pipeline": "sales", "amount_over": 500}}""",
            """record {"trigger":"w2","subscription":"sub_7","version":3}""",
        ], calls.Said);
    }

    [Fact]
    public async Task A_dial_that_changed_since_the_sheet_signs_nothing()
    {
        foreach (var changed in new[] { Dial(version: 4), Dial(config: """{"pipeline": "sales", "amount_over": 1}""") })
        {
            var calls = new Calls();
            calls.Reads.Enqueue(changed);
            var result = await KnobEvent.ApproveAsync(En, "t_1", "deals-k3", 3, Shown, calls.Make(), kept: null);
            Assert.Equal((false, "This event changed. Read the dial again before approving it."), (result.Approved, result.Why));
            Assert.Equal(["read dial"], calls.Said);
        }
        // The same filters in another key order are the same filters.
        var same = new Calls();
        same.Reads.Enqueue(Dial(config: """{"amount_over": 500, "pipeline": "sales"}"""));
        Assert.True((await KnobEvent.ApproveAsync(En, "t_1", "deals-k3", 3, Shown, same.Make(), kept: null)).Approved);
    }

    [Fact]
    public async Task Only_this_taf_s_connection_with_an_account_subscribes()
    {
        foreach (var grants in new List<PlanqueGrant>[] { [Grant("pending")], [Grant(agent: "other")], [Grant(account: null)], [] })
        {
            var calls = new Calls { Grants = grants };
            calls.Reads.Enqueue(Dial());
            var result = await KnobEvent.ApproveAsync(En, "t_1", "deals-k3", 3, Shown, calls.Make(), kept: null);
            Assert.Equal("Connect this app for the taf before approving its event.", result.Why);
            Assert.DoesNotContain("hello", calls.Said);
        }
    }

    [Fact]
    public async Task Windows_Hello_closed_says_nothing_and_sends_nothing()
    {
        var calls = new Calls { HelloClosed = true };
        calls.Reads.Enqueue(Dial());
        Assert.Equal(new KnobEventResult(false), await KnobEvent.ApproveAsync(En, "t_1", "deals-k3", 3, Shown, calls.Make(), kept: null));
        Assert.DoesNotContain(calls.Said, s => s.StartsWith("tap", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_subscription_tafs_cloud_didn_t_record_is_kept_and_recorded_again_never_made_twice()
    {
        var first = new Calls { Recorded = Dial() };
        first.Reads.Enqueue(Dial());
        var missed = await KnobEvent.ApproveAsync(En, "t_1", "deals-k3", 3, Shown, first.Make(), kept: null);
        Assert.Equal("The subscription was made, but the dial hasn't recorded it yet. Try again.", missed.Why);
        Assert.Equal("sub_7", missed.Kept?.Subscription);

        var again = new Calls();
        again.Reads.Enqueue(Dial());
        Assert.True((await KnobEvent.ApproveAsync(En, "t_1", "deals-k3", 3, Shown, again.Make(), missed.Kept)).Approved);
        Assert.Equal(["read dial", """record {"trigger":"w2","subscription":"sub_7","version":3}"""], again.Said);
    }

    [Fact]
    public async Task Planque_s_answer_without_an_id_says_so()
    {
        var calls = new Calls { Tap = (201, "{}") };
        calls.Reads.Enqueue(Dial());
        var result = await KnobEvent.ApproveAsync(En, "t_1", "deals-k3", 3, Shown, calls.Make(), kept: null);
        Assert.Equal("The connection didn't say which subscription it made. Try again.", result.Why);
        Assert.DoesNotContain(calls.Said, s => s.StartsWith("record", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Already_approved_elsewhere_is_done()
    {
        var calls = new Calls();
        calls.Reads.Enqueue(Dial(state: "approved", subscription: "sub_1"));
        Assert.True((await KnobEvent.ApproveAsync(En, "t_1", "deals-k3", 3, Shown, calls.Make(), kept: null)).Approved);
        Assert.Equal(["read dial"], calls.Said);
    }
}

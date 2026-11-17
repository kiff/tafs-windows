using System.Text;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>Trying a version, as the iPad's TrialTests: its Card read, confirmed as a trial, then opened; Install; Go back.</summary>
[Part("record")]
public class TafTrialsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static CloudRead Answer(string json) => CloudRead.Of(JsonDocument.Parse(json).RootElement);

    const string Open = """
        {"trial": {"id": "tr_1", "n": 2, "draft": "d_02", "session": "sess_t", "started": "2026-11-17T09:00:00Z", "used": "2026-11-17T09:00:00Z",
                   "label": "Trial", "title": "Trying version 2", "changed": "asks before refunds over 50 EUR", "try": "Refund ch_1 for 80 EUR",
                   "ends": "2026-11-24T09:00:00Z"}}
        """;

    /// <summary>Each call said in order, with KIFF's and tafs cloud's answers as a test sets them.</summary>
    sealed class Calls
    {
        public List<string> Said { get; } = [];
        public Dictionary<string, CloudRead> Cloud { get; } = [];
        public Dictionary<string, string> KiffDraft { get; } = [];
        public (int, string) Confirmed { get; set; } = (200, """{"id":"d_02","agent_id":"rae-k3f9","status":"trying","version":"v7"}""");
        public (int, string) Ended { get; set; } = (200, """{"status":"ended"}""");
        public Exception? SignFails { get; set; }
        public bool Current { get; set; } = true;
        public string? ConfirmBody { get; private set; }

        public TrialCalls Make() => new(
            (method, path, body) =>
            {
                Said.Add($"{method} {path}" + (body is null ? "" : " " + Encoding.UTF8.GetString(body)));
                return Task.FromResult(Cloud.TryGetValue($"{method} {path}", out var a) ? a : new CloudRead(null, "Not found.", 404));
            },
            path =>
            {
                Said.Add("KIFF GET " + path);
                return Task.FromResult(KiffDraft.TryGetValue(path, out var d) ? Answer(d) : new CloudRead(null, "No such draft.", 404));
            },
            (path, body) =>
            {
                Said.Add("sign " + path);
                return SignFails is { } e ? Task.FromException<(string, string)>(e) : Task.FromResult(("KIFF-Passkey-Assertion", "a"));
            },
            (path, body, signature) =>
            {
                Said.Add($"KIFF POST {path} {signature.Header}");
                ConfirmBody = Encoding.UTF8.GetString(body);
                return Task.FromResult(Confirmed);
            },
            path => { Said.Add("KIFF DELETE " + path); return Task.FromResult(Ended); },
            () => Current, e => e.Message);
    }

    static Calls Trying()
    {
        var calls = new Calls();
        calls.Cloud["POST /v1/tafs/depl_1/versions/2/try"] = Answer("""{"draft":"d_02"}""");
        calls.KiffDraft["/v1/me/agent-drafts/d_02"] = """{"id":"d_02","agent_id":"rae-k3f9","status":"draft","version":"v7"}""";
        calls.Cloud["PUT /v1/tafs/depl_1/trial"] = Answer(Open);
        return calls;
    }

    [Fact]
    public void An_open_trial_reads_with_its_guidance()
    {
        var t = TafTrials.Read(JsonDocument.Parse(Open).RootElement)!;
        Assert.Equal((2, "d_02", "sess_t"), (t.N, t.Draft, t.Session));
        Assert.Equal("Rae · v2", t.ChatName(En, "Rae"));
        Assert.Equal("Trying version 2 · ends 24 Nov if unused", t.Line(En, TimeZoneInfo.Utc));
        Assert.Equal([
            "What's new: asks before refunds over 50 EUR",
            "Try it with: Refund ch_1 for 80 EUR",
            "Rae keeps working as it is. What this version does is real, under its own Card. The trial ends 24 Nov if you don't use it. Install makes it the live version; Go back ends the trial.",
        ], t.Guide(En, "Rae", TimeZoneInfo.Utc));
        // An older tafs cloud: no guidance, its own words.
        var plain = TafTrials.Read(JsonDocument.Parse("""{"trial": {"id": "tr_1", "n": 3, "draft": "d_03"}}""").RootElement)!;
        Assert.Equal("Trying version 3", plain.Line(En, TimeZoneInfo.Utc));
        Assert.Equal("Give it a task that uses the change, as you would the live Rae.", plain.Guide(En, "Rae", TimeZoneInfo.Utc)[0]);
        Assert.Null(TafTrials.Read(JsonDocument.Parse("""{"trial": null}""").RootElement));
        Assert.Null(TafTrials.Read(JsonDocument.Parse("""{"trial": {"n": 2}}""").RootElement));
    }

    [Fact]
    public void Home_says_each_taf_s_open_trial()
    {
        var home = Tafs.Core.Home.HomeList.Read(En, JsonDocument.Parse("""
            {"tafs":[
              {"id":"t1","name":"Rae","state":"working","trial":{"id":"tr_1","n":2,"draft":"d_02","title":"Trying version 2"}},
              {"id":"t2","name":"Ana","state":"idle","trial":null},
              {"id":"t3","name":"Bo","state":"idle","trial":"not one"}]}
            """).RootElement, TimeZoneInfo.Utc);
        Assert.Equal(2, home.Tafs[0].Trial!.N);
        Assert.Null(home.Tafs[1].Trial);
        Assert.Null(home.Tafs[2].Trial);
    }

    [Fact]
    public void Its_paths()
    {
        Assert.Equal("/v1/tafs/t%2F1/versions/2/try", TafTrials.TryPath("t/1", 2));
        Assert.Equal("/v1/tafs/t1/trial/messages", TafTrials.MessagesPath("t1"));
        Assert.Equal("/v1/tafs/t1/trial/conversation", TafTrials.ConversationPath("t1"));
        Assert.Equal("/v1/tafs/t1/trial/install", TafTrials.InstallPath("t1"));
        Assert.Equal("/v1/me/agent-drafts/d_02/trial", TafTrials.EndPath("d_02"));
        Assert.Equal("""{"version":"v7","as":"trial"}""", Encoding.UTF8.GetString(TafTrials.Confirm("d_02", "v7", asTrial: true).Body));
        Assert.Equal("""{"version":"v7"}""", Encoding.UTF8.GetString(TafTrials.Confirm("d_02", "v7", asTrial: false).Body));
    }

    [Fact]
    public async Task Its_Card_is_read_before_anything_is_signed()
    {
        var calls = Trying();
        var (draft, why) = await TafTrials.CardAsync(En, "depl_1", 2, calls.Make());
        Assert.Null(why);
        Assert.Equal("v7", draft!.Version);
        Assert.DoesNotContain(calls.Said, s => s.StartsWith("sign", StringComparison.Ordinal));

        var unread = new Calls();
        unread.Cloud["POST /v1/tafs/depl_1/versions/2/try"] = new CloudRead(null, "This version can't be tried.", 409);
        Assert.Equal("Version 2's trial Card couldn't be read. This version can't be tried.", (await TafTrials.CardAsync(En, "depl_1", 2, unread.Make())).Why);
    }

    /// <summary>The draft the owner read: d_02 at v7.</summary>
    static readonly AgentDraft Shown = new("d_02", "rae-k3f9", "draft", "v7");

    [Fact]
    public async Task Start_trying_confirms_its_Card_as_a_trial_then_opens_it()
    {
        var calls = Trying();
        var started = await TafTrials.StartAsync(En, "depl_1", 2, Shown, calls.Make());
        Assert.Null(started.Why);
        Assert.Equal(2, started.Open!.N);
        // The draft read, by its id: never drafted again.
        Assert.Equal([
            "KIFF GET /v1/me/agent-drafts/d_02",
            "sign /v1/me/agent-drafts/d_02/confirm",
            "KIFF POST /v1/me/agent-drafts/d_02/confirm KIFF-Passkey-Assertion",
            """PUT /v1/tafs/depl_1/trial {"n":2,"draft":"d_02"}""",
        ], calls.Said);
        Assert.Equal("""{"version":"v7","as":"trial"}""", calls.ConfirmBody);
    }

    [Fact]
    public async Task A_Card_changed_since_it_was_shown_is_never_signed()
    {
        var calls = Trying();
        calls.KiffDraft["/v1/me/agent-drafts/d_02"] = """{"id":"d_02","agent_id":"rae-k3f9","status":"draft","version":"v8"}""";
        var started = await TafTrials.StartAsync(En, "depl_1", 2, Shown, calls.Make());
        Assert.Equal("v8", started.Changed!.Version);
        Assert.Equal("What you're asked to approve changed since it was shown. Read it again, then start.", started.Why);
        Assert.DoesNotContain(calls.Said, s => s.StartsWith("sign", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_KIFF_that_doesnt_start_a_trial_opens_none()
    {
        var calls = Trying();
        calls.Confirmed = (200, """{"id":"d_02","agent_id":"rae-k3f9","status":"confirmed","version":"v7"}""");
        var started = await TafTrials.StartAsync(En, "depl_1", 2, Shown, calls.Make());
        Assert.Null(started.Open);
        Assert.StartsWith("KIFF didn't start a trial: it issued these Cards as the taf's own.", started.Why);
        Assert.DoesNotContain(calls.Said, s => s.StartsWith("PUT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Hello_closed_sends_nothing_and_says_nothing()
    {
        var calls = Trying();
        calls.SignFails = new OperationCanceledException();
        Assert.Equal(new TrialStart(null, null, null), await TafTrials.StartAsync(En, "depl_1", 2, Shown, calls.Make()));
        Assert.DoesNotContain(calls.Said, s => s.StartsWith("KIFF POST", StringComparison.Ordinal));

        var refused = Trying();
        refused.SignFails = new PasskeyRefusedException("only this PC's passkey confirms a Card change");
        Assert.Equal("Trying version 2 didn't start. only this PC's passkey confirms a Card change",
            (await TafTrials.StartAsync(En, "depl_1", 2, Shown, refused.Make())).Why);
    }

    [Fact]
    public async Task Another_draft_with_the_same_version_is_shown_again_and_never_signed()
    {
        // KIFF answers for the shown id with another draft: the same version, but not the one read.
        var calls = Trying();
        calls.KiffDraft["/v1/me/agent-drafts/d_02"] = """{"id":"d_09","agent_id":"rae-k3f9","status":"draft","version":"v7"}""";
        var started = await TafTrials.StartAsync(En, "depl_1", 2, Shown, calls.Make());
        Assert.Equal("d_09", started.Changed!.Id);
        Assert.DoesNotContain(calls.Said, s => s.StartsWith("sign", StringComparison.Ordinal) || s.StartsWith("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Install_confirms_its_Card_live_first_then_tafs_cloud_makes_it_live()
    {
        var open = TafTrials.Read(JsonDocument.Parse(Open).RootElement)!;
        var calls = new Calls();
        calls.KiffDraft["/v1/me/agent-drafts/d_02"] = """{"id":"d_02","agent_id":"rae-k3f9","status":"trying","version":"v7"}""";
        calls.Confirmed = (200, """{"id":"d_02","agent_id":"rae-k3f9","status":"confirmed","version":"v7"}""");
        calls.Cloud["POST /v1/tafs/depl_1/trial/install"] = Answer("""{"live": 2, "ended": {"why": "installed"}}""");
        Assert.Equal(new TrialInstall(true), await TafTrials.InstallAsync(En, "depl_1", open, "v7", calls.Make()));
        Assert.Equal([
            "KIFF GET /v1/me/agent-drafts/d_02",
            "sign /v1/me/agent-drafts/d_02/confirm",
            "KIFF POST /v1/me/agent-drafts/d_02/confirm KIFF-Passkey-Assertion",
            "POST /v1/tafs/depl_1/trial/install",
        ], calls.Said);
        Assert.Equal("""{"version":"v7"}""", calls.ConfirmBody);

        // KIFF has it confirmed already: nothing new is granted, so no Windows Hello; tafs cloud finishes it.
        var confirmed = new Calls();
        confirmed.KiffDraft["/v1/me/agent-drafts/d_02"] = """{"id":"d_02","agent_id":"rae-k3f9","status":"confirmed","version":"v7"}""";
        confirmed.Cloud["POST /v1/tafs/depl_1/trial/install"] = Answer("""{"live": 2}""");
        Assert.True((await TafTrials.InstallAsync(En, "depl_1", open, null, confirmed.Make())).Done);
        Assert.Equal(["KIFF GET /v1/me/agent-drafts/d_02", "POST /v1/tafs/depl_1/trial/install"], confirmed.Said);
    }

    [Fact]
    public async Task A_trial_Card_that_moved_since_it_was_shown_is_shown_again_and_never_signed()
    {
        var open = TafTrials.Read(JsonDocument.Parse(Open).RootElement)!;
        var calls = new Calls();
        calls.KiffDraft["/v1/me/agent-drafts/d_02"] = """{"id":"d_02","agent_id":"rae-k3f9","status":"trying","version":"v8"}""";
        var result = await TafTrials.InstallAsync(En, "depl_1", open, "v7", calls.Make());
        Assert.Equal("v8", result.Changed!.Version);
        Assert.Equal(["KIFF GET /v1/me/agent-drafts/d_02"], calls.Said);
        // Nothing shown, nothing signed.
        var unshown = new Calls();
        unshown.KiffDraft["/v1/me/agent-drafts/d_02"] = calls.KiffDraft["/v1/me/agent-drafts/d_02"];
        Assert.Equal("v8", (await TafTrials.InstallAsync(En, "depl_1", open, null, unshown.Make())).Changed!.Version);
    }

    [Fact]
    public async Task Go_back_ends_its_Card_in_KIFF_then_tafs_cloud_ends_the_trial()
    {
        var open = TafTrials.Read(JsonDocument.Parse(Open).RootElement)!;
        var calls = new Calls();
        calls.Cloud["DELETE /v1/tafs/depl_1/trial"] = Answer("""{"ended": {"why": "back"}}""");
        Assert.Null(await TafTrials.GoBackAsync(En, "depl_1", open, calls.Make()));
        Assert.Equal(["KIFF DELETE /v1/me/agent-drafts/d_02/trial", "DELETE /v1/tafs/depl_1/trial"], calls.Said);

        // KIFF ended it already: tafs cloud still records it.
        var already = new Calls { Ended = (409, """{"error":"not trying"}""") };
        already.Cloud["DELETE /v1/tafs/depl_1/trial"] = Answer("""{"ended": {"why": "back"}}""");
        Assert.Null(await TafTrials.GoBackAsync(En, "depl_1", open, already.Make()));

        var refused = new Calls { Ended = (403, """{"error":"Not your draft."}""") };
        Assert.Equal("The trial didn't end. Not your draft.", await TafTrials.GoBackAsync(En, "depl_1", open, refused.Make()));
        Assert.DoesNotContain(refused.Said, s => s.StartsWith("DELETE /v1/tafs", StringComparison.Ordinal));
    }
}

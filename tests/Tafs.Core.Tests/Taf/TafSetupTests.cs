using System.Text.Json;
using Tafs.Core.Contracts;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

[Part("record")]
public class TafSetupTests
{
    static SetupPage Contract(string file) => TafSetup.Read(ContractFile.Answer(SharedFiles.Contract(file)));

    static SetupPage Json(string json) => TafSetup.Read(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void With_nothing_connected_it_says_why_and_the_Card_unit_exception_waits()
    {
        var setup = Contract("taf-setup.json");

        Assert.Empty(setup.Connections);
        Assert.Equal("Connect this phone to read its connected apps", setup.ConnectionsError);
        Assert.Equal(new SetupWorkflow("Workflow", "Ask Coach to set its workflow", []), setup.Workflow with { Steps = [] });
        Assert.Equal(new SetupMemory(0, "0 notes", null), setup.Memory);
        Assert.Equal(new SetupVersions("Changes to how it runs", "Runs its original definition", null, 0), setup.Versions);
        var ask = Assert.Single(setup.CardUnitExceptions);
        Assert.Equal(("exc-example", "Rae needs a USD limit", "Add a USD limit to this action", "Dismiss", false),
            (ask.Id, ask.Title, ask.ActionTitle, ask.DismissTitle, ask.CanApproveOnce));
        Assert.Equal("Tried 4 times since 28 Oct · last 09:00", ask.History);
        Assert.StartsWith("Add a USD limit to this action for Rae.", ask.CoachText);
        Assert.Null(setup.ConnectAgain);
    }

    [Fact]
    public void A_connected_app_reads_with_its_status_actions_and_week()
    {
        var gmail = Assert.Single(Contract("taf-setup-connected.json").Connections);

        Assert.Equal(("grt_gmail", "Gmail", "connected", "Connected", false, "1 action"),
            (gmail.Id, gmail.Name, gmail.Status, gmail.StatusWords, gmail.Reconnect, gmail.ActionWords));
        var action = Assert.Single(gmail.Actions);
        Assert.Equal(("fetch_emails", "Fetch emails"), (action.Name, action.Title));
        Assert.Equal(new[] { "See its Card" }, action.Limits);
        Assert.Equal("0 calls · 0 done · 0 held in the last 7 days", gmail.StatsWords);
        Assert.Equal("tools=fetch_emails&from=2026-10-22T10%3A00%3A00.000Z", gmail.ActivityQuery);
    }

    [Fact]
    public void With_its_sign_in_expired_the_taf_has_its_own_Connect_again()
    {
        var setup = Contract("taf-setup-reconnect.json");

        var gmail = Assert.Single(setup.Connections);
        Assert.Equal(("needs_reconnect", "Sign in again", true), (gmail.Status, gmail.StatusWords, gmail.Reconnect));
        Assert.Equal("Ask Coach to set its workflow", gmail.WorkflowWords);
        Assert.Equal(new ConnectAgain("grt_gmail", "Gmail", "Connect again",
            "Gmail's sign-in has expired, so Rae can't use it. Connect again to sign in.",
            "Gmail is connected again. Rae can use it."), setup.ConnectAgain);
    }

    /// <summary>As the iPad's ConnectAgain.usable: without its connection, title or words, there's no Connect again.</summary>
    [Theory]
    [InlineData("""{"app": "Gmail", "title": "Connect again", "words": "Sign in."}""")]
    [InlineData("""{"grant": "grt_gmail", "title": "  ", "words": "Sign in."}""")]
    [InlineData("""{"grant": "grt_gmail", "title": "Connect again"}""")]
    [InlineData("\"Connect again\"")]
    public void A_Connect_again_it_can_t_use_reads_as_none(string again) =>
        Assert.Null(Json($$"""{"connect_again": {{again}}}""").ConnectAgain);

    [Fact]
    public void What_it_can_t_read_is_left_out_never_the_page_with_it()
    {
        var setup = Json("""
            {"connections": [{"id": "grt_a", "name": "Stripe", "actions": [{"title": "No name"}, {"name": "refund", "limits": ["€40 each", 3]}]},
                             {"id": "grt_b"}, "Gmail"],
             "workflow": {"title": "Workflow", "summary": "2 steps", "steps": [{"number": 1, "label": "Read the case", "running": true, "app": "Stripe"}, {"label": "No number"}]},
             "memory": {"count": "three", "title": "3 notes", "latest": "Explain in one line"},
             "card_unit_exceptions": [{"id": "e", "title": "Needs a USD limit"}],
             "versions": "v2"}
            """);

        var stripe = Assert.Single(setup.Connections);
        var refund = Assert.Single(stripe.Actions);
        Assert.Equal(("refund", "refund"), (refund.Name, refund.Title));
        Assert.Equal(new[] { "€40 each" }, refund.Limits);
        Assert.Equal(("", ""), (stripe.StatusWords, stripe.StatsWords));
        Assert.Equal(new WorkflowStep(1, "Read the case", "", "Stripe", true), Assert.Single(setup.Workflow.Steps));
        Assert.Equal(new SetupMemory(0, "3 notes", "Explain in one line"), setup.Memory);
        Assert.Empty(setup.CardUnitExceptions);
        Assert.Equal(new SetupVersions("", "", null, 0), setup.Versions);
        Assert.Null(setup.ConnectionsError);
    }

    [Fact]
    public void An_answer_that_isn_t_an_object_reads_as_an_empty_Setup()
    {
        var setup = Json("[]");
        Assert.Empty(setup.Connections);
        Assert.Empty(setup.Workflow.Steps);
        Assert.Null(setup.ConnectAgain);
    }

    [Fact]
    public void The_sample_s_notes_are_the_iPad_s_with_their_paths_as_words()
    {
        var now = new DateTimeOffset(2026, 11, 15, 2, 57, 0, TimeSpan.Zero);
        var notes = TafSetup.SampleNotes(WordBook.Load(SharedFiles.Strings, "en"), now);

        Assert.Equal(new[] { "You'd rather I explain a decision in one line than in a paragraph.",
            "Customers who write every week get a reply the same morning.", "Ask before anything unusual, even when it fits the Card." },
            notes.Select(n => n.Content));
        var lines = notes.Select(n => TafSetup.NoteLine(n, TimeZoneInfo.Utc)).ToList();
        Assert.Equal(new[] { "how you like it · 01:57", "customers · regulars · 02:57" }, lines.Take(2));
        Assert.Equal(notes[2].Path.Trim('/')[..^3].Replace('-', ' ') + " · 02:57", lines[2]);
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void The_sample_s_job_and_notes_are_there_in_every_shipped_language(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        Assert.False(string.IsNullOrWhiteSpace(TafSetup.SampleJob(w)));
        Assert.All(TafSetup.SampleNotes(w, DateTimeOffset.UtcNow), n => Assert.False(string.IsNullOrWhiteSpace(n.Content)));
    }

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    [Fact]
    public void A_connection_reads_its_action_count_and_whether_it_runs()
    {
        var gmail = Assert.Single(Contract("taf-setup-connected.json").Connections);
        Assert.Equal((1, false), (gmail.ActionCount, gmail.Running));
        Assert.True(Json("""{"connections": [{"id": "g", "name": "Gmail", "action_count": 2, "running": true}]}""").Connections[0].Running);
    }

    static SetupConnection App(string id, string name, int actions = 0, bool reconnect = false) =>
        new(id, name, "", "", reconnect, "", [], null, "", null, actions);

    /// <summary>The iPad's nodes: one an app, in the order of its first connection; the one with actions shown, else one to sign in again.</summary>
    [Fact]
    public void The_ring_has_one_node_an_app_the_one_with_actions_in_front()
    {
        var nodes = TafSetup.Nodes([App("s1", "Stripe"), App("g1", "Gmail", reconnect: true), App("s2", "Stripe", actions: 2), App("g2", "Gmail")]);

        Assert.Equal(new[] { "s2", "g1" }, nodes.Select(n => n.App.Id));
        Assert.Equal(new[] { "s1" }, nodes[0].Others.Select(o => o.Id));
        Assert.Equal(new[] { "g2" }, nodes[1].Others.Select(o => o.Id));
        Assert.Equal("g", TafSetup.Nodes([App("g", "Gmail"), App("h", "Gmail")])[0].App.Id);
    }

    /// <summary>As the iPad's loadTafSetup: the page once read, kept when a later read fails, which says why.</summary>
    [Fact]
    public async Task Setup_reads_without_the_proof_and_a_failure_keeps_the_page()
    {
        var asked = new List<string>();
        var answer = CloudRead.Of(ContractFile.Answer(SharedFiles.Contract("taf-setup-connected.json")));
        var setup = new SetupRead();
        Assert.Null(setup.Page);

        await setup.LoadAsync("taf/1", path => { asked.Add(path); return Task.FromResult(answer); });
        Assert.Equal(new[] { "/v1/tafs/taf%2F1/setup" }, asked);
        Assert.Equal("Gmail", setup.Page?.Connections[0].Name);
        Assert.Null(setup.Error);

        await setup.LoadAsync("taf/1", _ => Task.FromResult(CloudRead.Refused(En, 503, """{"words": "Try again in a minute."}""")));
        Assert.Equal("Gmail", setup.Page?.Connections[0].Name);
        Assert.Equal("Try again in a minute.", setup.Error);
    }

    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
}

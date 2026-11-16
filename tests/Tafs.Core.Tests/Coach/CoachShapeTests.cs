using System.Text;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Coach;
using Tafs.Core.Contracts;
using Tafs.Core.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>"Taking shape", as the iPad's SetupShape: what Coach's conversation has made, from what this PC holds.</summary>
[Part("coach")]
public class CoachShapeTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static CoachThread Contract() => CoachThread.Read(ContractFile.Answer(SharedFiles.Contract("coach-conversation.json")))!;

    static PlanqueGrant Grant(string id, string status, string tools = "") =>
        PlanqueGrants.Read(Encoding.UTF8.GetBytes($$"""{"grants": [{"id": "{{id}}", "status": "{{status}}", "text": "t", "digest": "d", "reason": "Its sign-in expired", "request": {"server": "https://mcp.stripe.com", "agent": "refunds", "tools": [{{tools}}]}, "vendor": "Stripe", "events": []}]}"""))![0];

    /// <summary>No plan shown: what Coach asked, and where each stands; the taf's name from its result; the grant the approval names.</summary>
    [Fact]
    public void Without_a_plan_the_asks_make_the_steps()
    {
        var shape = CoachShape.Of(En, Contract(), [Grant("grt_1", "connected", """{"tool": "refund_payment", "title": "Refund a payment"}""")], new Dictionary<string, AgentDraft>());
        Assert.Equal("Refunds", shape.Name);
        Assert.Null(shape.Title);
        Assert.Equal([new ShapeStep("Refunds' limits", ShapeStepState.Done), new ShapeStep("Approve Stripe", ShapeStepState.Done)], shape.Steps);
        var tool = Assert.Single(shape.Tools);
        Assert.Equal(("grt_1", "Stripe", ShapeToolState.Connected), (tool.Id, tool.Vendor, tool.State));
        Assert.Equal(["Refund a payment"], tool.Actions);
        Assert.Null(shape.Card);
        Assert.False(shape.IsEmpty);
    }

    [Theory]
    [InlineData("failed", ShapeToolState.Failed)]
    [InlineData("listed", ShapeToolState.SignedIn)]
    [InlineData("approved", ShapeToolState.Waiting)]
    public void A_tool_reads_where_its_grant_stands(string status, ShapeToolState state)
    {
        var tool = Assert.Single(CoachShape.Of(En, Contract(), [Grant("grt_1", status)], new Dictionary<string, AgentDraft>()).Tools);
        Assert.Equal(state, tool.State);
        Assert.Equal(state == ShapeToolState.Failed ? "Its sign-in expired" : null, tool.Why);
        Assert.Empty(tool.Actions);
    }

    /// <summary>With a plan: its steps, its title; the active step needs you while a card waits for you.</summary>
    [Fact]
    public void With_a_plan_its_active_step_needs_you_while_a_card_waits()
    {
        var answer = System.Text.Json.Nodes.JsonNode.Parse(ContractFile.Answer(SharedFiles.Contract("coach-conversation.json")).GetRawText())!;
        var plan = new System.Text.Json.Nodes.JsonObject
        {
            ["at"] = "2026-10-16T12:00:05.000Z", ["session"] = "sess1", ["run"] = "run2", ["seq"] = 1, ["key"] = "sess1#plan",
            ["component"] = System.Text.Json.Nodes.JsonNode.Parse("""{"type": "steps", "id": "plan", "title": "Connect Stripe", "steps": [{"label": "Ask the limits", "status": "done"}, {"label": "Connect Stripe", "status": "active"}, {"label": "Hand it over", "status": "pending"}, {"label": "Old step", "status": "skipped"}]}"""),
        };
        answer["components"]!.AsArray().Add(plan);
        // The approval waits for the person: no answer yet.
        foreach (var c in answer["components"]!.AsArray().Concat(answer["items"]!.AsArray().Select(i => i?["card"])))
            if (c?["component"]?["type"]?.GetValue<string>() == "approval") c.AsObject().Remove("answer");
        var thread = CoachThread.Read(JsonDocument.Parse(answer.ToJsonString()).RootElement)!;

        var shape = CoachShape.Of(En, thread, [], new Dictionary<string, AgentDraft>());
        Assert.Equal("Connect Stripe", shape.Title);
        Assert.Equal([ShapeStepState.Done, ShapeStepState.NeedsYou, ShapeStepState.ToDo, ShapeStepState.Skipped], shape.Steps.Select(s => s.State));
    }

    /// <summary>The Card is the latest an approval names that KIFF has shown: a draft until confirmed; one with no tools isn't shown.</summary>
    [Theory]
    [InlineData("pending", false)]
    [InlineData("confirmed", true)]
    public void The_card_is_the_draft_an_approval_names(string status, bool confirmed)
    {
        var answer = System.Text.Json.Nodes.JsonNode.Parse(ContractFile.Answer(SharedFiles.Contract("coach-conversation.json")).GetRawText())!;
        foreach (var c in answer["components"]!.AsArray().Concat(answer["items"]!.AsArray().Select(i => i?["card"])))
            if (c?["component"]?["type"]?.GetValue<string>() == "approval") c["component"]!["draft"] = "drf_1";
        var thread = CoachThread.Read(JsonDocument.Parse(answer.ToJsonString()).RootElement)!;
        AgentDraft Draft(string more = "") => KiffDrafts.Read(JsonDocument.Parse($$"""{"id": "drf_1", "agent_id": "refunds", "status": "{{status}}", "version": "v1", "label": "Refunds", "cards": [{"grants": [{"action": "stripe.refund_payment", "title": "Refund a payment"}]}]{{more}}}""").RootElement)!;

        var card = CoachShape.Of(En, thread, [], new Dictionary<string, AgentDraft> { ["drf_1"] = Draft() }).Card!;
        Assert.Equal(("Refunds' Card", confirmed), (card.Title, card.Confirmed));
        Assert.NotEmpty(card.Lines);
        Assert.Null(CoachShape.Of(En, thread, [], new Dictionary<string, AgentDraft> { ["drf_1"] = Draft(""", "no_tools": true""") }).Card);
        // The result's draft isn't an approval's: none shown from it alone.
        Assert.Null(CoachShape.Of(En, Contract(), [], new Dictionary<string, AgentDraft> { ["drf_s1"] = Draft() }).Card);
    }

    /// <summary>Beside the conversation from 780 across, as the iPad's; narrower, the conversation alone.</summary>
    [Theory]
    [InlineData(779, false)]
    [InlineData(780, true)]
    [InlineData(1400, true)]
    public void It_shows_from_780_across(double width, bool shown) => Assert.Equal(shown, CoachScreen.Shaping(width));

    [Fact]
    public void An_empty_conversation_has_taken_no_shape()
    {
        var empty = CoachThread.Read(JsonDocument.Parse("""{"messages": [], "components": [], "items": []}""").RootElement)!;
        Assert.True(CoachShape.Of(En, empty, [], new Dictionary<string, AgentDraft>()).IsEmpty);
    }
}

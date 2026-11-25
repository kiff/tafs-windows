using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Coach;
using Tafs.Core.Contracts;
using Tafs.Core.Tests.Strings;

namespace Tafs.Core.Tests.Coach;

/// <summary>
/// The Card step before Coach's first approval, read from tafs cloud's own first starts at the pin (setup-start.json, the pair;
/// setup-start-draft-alone.json, the draft alone), and its shown = signed check failing closed, as the iPhone's CardStepTests.
/// </summary>
[Part("coach")]
public class CardStepTests
{
    const string Mcp = "https://mcp.tafs.example/mcp";

    static JsonElement Setup(string file) => ContractFile.Answer(SharedFiles.Contract(file));

    static FirstStart Start(string file = "setup-start.json") => FirstStart.Read(Setup(file))!;

    [Theory]
    [InlineData("setup-start.json")]
    [InlineData("setup-start-draft-alone.json")]
    public void Tafs_cloud_s_first_starts_carry_every_line_and_their_list_is_what_they_sign(string file)
    {
        var start = Start(file);
        Assert.True(start.CardStep);
        var w = start.Words!;
        Assert.All(new[] { w.Title, w.Allows, w.Asks, w.Sign, w.Stop, w.Exact, w.Give, w.Locked }, s => Assert.NotEqual("", s));
        // The plain list, each tool with its Card, limit and asks, is exactly the draft's Cards.
        var shown = CardStepCheck.Shown(start.Groups)!;
        Assert.NotEmpty(shown);
        Assert.True(shown.SetEquals(CardStepCheck.SignedBy(start.Draft!.Body, start.AgentId)!));
        Assert.True(CardStepCheck.Matches(start.Groups, start.Draft.Body, start.AgentId));
    }

    [Fact]
    public void This_PC_s_button_and_sign_line_are_the_passkey_s_never_the_phone_s()
    {
        var words = Setup("setup-start.json").GetProperty("start").GetProperty("words");
        var w = Start().Words!;
        Assert.Equal(words.GetProperty("buttons").GetProperty("passkey").GetString(), w.Give);
        Assert.Equal(words.GetProperty("sign_passkey").GetString(), w.Sign);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("exact")]
    [InlineData("sign_passkey")]
    [InlineData("lines.allows")]
    [InlineData("lines.asks")]
    [InlineData("lines.stop")]
    [InlineData("buttons.passkey")]
    public void Without_any_line_or_this_PC_s_button_it_isn_t_the_Card_step(string missing)
    {
        var setup = JsonNode.Parse(Setup("setup-start.json").GetRawText())!;
        var words = setup["start"]!["words"]!.AsObject();
        if (missing.Split('.') is [var group, var key]) words[group]!.AsObject().Remove(key);
        else words.Remove(missing);
        var start = FirstStart.Read(JsonDocument.Parse(setup.ToJsonString()).RootElement)!;
        Assert.False(start.CardStep);
        // Start's screen is as before: the lead and its button still read.
        Assert.True(start.OneScreen);
    }

    [Fact]
    public void A_blank_locked_line_keeps_the_Card_step()
    {
        var setup = JsonNode.Parse(Setup("setup-start.json").GetRawText())!;
        setup["start"]!["words"]!["locked"] = " ";
        var start = FirstStart.Read(JsonDocument.Parse(setup.ToJsonString()).RootElement)!;
        Assert.True(start.CardStep);
        Assert.Equal("", start.Words!.Locked);
    }

    /// <summary>The pair's draft, as JSON to change, and the check over the step against it.</summary>
    static JsonObject Draft() => JsonNode.Parse(Start().Draft!.Body)!.AsObject();

    static bool Signs(JsonObject draft) => CardStepCheck.Matches(Start().Groups, Encoding.UTF8.GetBytes(draft.ToJsonString()), Start().AgentId);

    static JsonObject FirstCard(JsonObject draft) => draft["cards"]![0]!.AsObject();

    static JsonObject Aggregate(JsonObject draft) => FirstCard(draft)["aggregates"]![0]!.AsObject();

    public static TheoryData<string, Action<JsonObject>> Changes => new()
    {
        { "a higher limit", d => Aggregate(d)["limit"] = Aggregate(d)["limit"]!.GetValue<int>() + 1 },
        { "a fractional limit", d => Aggregate(d)["limit"] = 1.5 },
        { "a boolean limit", d => Aggregate(d)["limit"] = true },
        { "another window", d => Aggregate(d)["window"] = "rolling_day" },
        { "a sum", d => Aggregate(d)["quantity"] = "sum" },
        { "a measure", d => Aggregate(d)["measure"] = "amount" },
        { "a second aggregate", d => FirstCard(d)["aggregates"]!.AsArray().Add(Aggregate(d).DeepClone()) },
        { "no aggregate", d => FirstCard(d)["aggregates"] = new JsonArray() },
        { "a per-call cap", d => FirstCard(d)["per_call"] = 1 },
        { "an extra action", d => FirstCard(d)["grants"]!.AsArray().Add(new JsonObject { ["action"] = "money_move" }) },
        { "an action on two Cards", d => d["cards"]![1]!["grants"]!.AsArray().Add(FirstCard(d)["grants"]![0]!.DeepClone()) },
        { "more on a grant", d => FirstCard(d)["grants"]![0]!["resources"] = new JsonArray("all") },
        { "another subject", d => FirstCard(d)["subject"] = "someone-else" },
        { "no domain", d => FirstCard(d).Remove("domain") },
        { "another domain", d => FirstCard(d)["domain"] = "api" },
        { "on_exceed flipped", d => FirstCard(d)["on_exceed"] = FirstCard(d)["on_exceed"]!.GetValue<string>() == "owner" ? "refuse" : "owner" },
        { "an unknown on_exceed", d => FirstCard(d)["on_exceed"] = "allow" },
        { "an environment", d => FirstCard(d)["environment"] = "live" },
        { "a missing Card", d => d["cards"]!.AsArray().RemoveAt(0) },
        { "an extra Card", d => d["cards"]!.AsArray().Add(FirstCard(d).DeepClone()) },
        { "a renamed Card", d => FirstCard(d)["id"] = "coach-everything" },
        { "another agent's draft", d => d["agent_id"] = "another-agent" },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Each_change_to_the_signed_Cards_fails_closed(string change, Action<JsonObject> edit)
    {
        var draft = Draft();
        Assert.True(Signs(draft));
        edit(draft);
        Assert.False(Signs(draft), change);
    }

    [Fact]
    public void A_hold_s_expiry_only_on_a_Card_that_waits_for_the_person()
    {
        var draft = Draft();
        var refusing = draft["cards"]!.AsArray().Select(c => c!.AsObject()).First(c => c["on_exceed"]!.GetValue<string>() == "refuse");
        refusing["hold_expiry"] = "PT1H";
        Assert.False(Signs(draft));
    }

    [Fact]
    public void A_draft_that_doesn_t_read_signs_nothing() =>
        Assert.False(CardStepCheck.Matches(Start().Groups, "not json"u8.ToArray(), Start().AgentId));

    [Fact]
    public void A_list_that_doesn_t_say_it_all_signs_nothing()
    {
        var start = Start();
        var tool = start.Groups[0].Tools[0];
        foreach (var partial in new[] { tool with { Limit = null }, tool with { Card = null }, tool with { Asks = null }, tool with { Tool = "" },
            tool with { Limit = tool.Limit + 1 }, tool with { Asks = !tool.Asks } })
        {
            var groups = start.Groups.Select((g, i) => i == 0 ? g with { Tools = [partial, .. g.Tools.Skip(1)] } : g).ToList();
            Assert.False(CardStepCheck.Matches(groups, start.Draft!.Body, start.AgentId));
        }
        // A tool listed twice.
        var twice = start.Groups.Select((g, i) => i == 0 ? g with { Tools = [.. g.Tools, g.Tools[0]] } : g).ToList();
        Assert.False(CardStepCheck.Matches(twice, start.Draft!.Body, start.AgentId));
    }

    [Fact]
    public void Only_the_tools_in_the_draft_are_compared()
    {
        var start = Start();
        // A tool marked not in this draft is left out of the comparison.
        var extra = new StepTool("Move money", "", "", "money_move", "coach-money", 1, true, InDraft: false);
        var groups = start.Groups.Select((g, i) => i == 0 ? g with { Tools = [.. g.Tools, extra] } : g).ToList();
        Assert.True(CardStepCheck.Matches(groups, start.Draft!.Body, start.AgentId));
    }
}

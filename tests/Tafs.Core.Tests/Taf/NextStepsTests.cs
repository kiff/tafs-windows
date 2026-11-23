using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;

namespace Tafs.Core.Tests.Taf;

/// <summary>An Activity line's next step, as the iPad's NextStepWords and NextStepRow.</summary>
[Part("record")]
public sealed class NextStepsTests
{
    static PlanqueGrant Grant(string id, string status, string agent, params string[] tools) =>
        new(id, status, "", "d", new GrantRequest("gmail", agent, tools.Select(t => new GrantTool(t)).ToList()), []);

    [Fact]
    public void An_entry_s_next_step_is_read_with_its_kind_and_words()
    {
        var line = TafActivity.Line(JsonDocument.Parse("""
            {"id": "e1", "title": "Refund refused", "detail": "Stripe signed out", "next": {"kind": "reconnect", "words": "Sign in to Stripe again", "tool": "stripe__create_refund"}}
            """).RootElement)!;
        Assert.Equal(new NextStep("reconnect", "Sign in to Stripe again", "stripe__create_refund"), line.Next);
        Assert.Null(TafActivity.Line(JsonDocument.Parse("""{"id": "e2", "title": "t", "next": {"kind": "reconnect"}}""").RootElement)!.Next);
        Assert.Null(TafActivity.Line(JsonDocument.Parse("""{"id": "e3", "title": "t"}""").RootElement)!.Next);
    }

    [Fact]
    public void A_reconnect_signs_in_again_the_taf_s_own_connection_that_needs_it_else_opens_Connect_apps()
    {
        var mine = Grant("grt_mine", "needs_reconnect", "agt_refunds", "stripe__create_refund");
        var other = Grant("grt_other", "needs_reconnect", "agt_other", "stripe__create_refund");
        var next = new NextStep("reconnect", "Sign in again", "stripe__create_refund");
        Assert.Equal(new NextStepAction.SignInAgain(mine), NextSteps.Action(next, "agt_refunds", [other, mine], "f"));
        // Another taf's, when this one has none.
        Assert.Equal(new NextStepAction.SignInAgain(other), NextSteps.Action(next, "agt_refunds", [other], "f"));
        // Connected, ended, or no tool named: Connect apps.
        Assert.IsType<NextStepAction.ConnectApps>(NextSteps.Action(next, "agt_refunds", [Grant("g", "connected", "agt_refunds", "stripe__create_refund")], "f"));
        Assert.IsType<NextStepAction.ConnectApps>(NextSteps.Action(next, "agt_refunds", [Grant("g", "disconnected", "agt_refunds", "stripe__create_refund")], "f"));
        Assert.IsType<NextStepAction.ConnectApps>(NextSteps.Action(next with { Tool = null }, "agt_refunds", [mine], "f"));
    }

    [Theory]
    [InlineData("reconnect", true, false, true)]
    [InlineData("ask_coach", true, true, true)]
    [InlineData("ask_coach", true, false, false)]
    [InlineData("reconnect", false, true, false)]
    [InlineData("try_later", true, true, false)]
    public void A_next_step_is_a_button_only_for_whoever_manages_the_taf(string kind, bool manages, bool mayUseCoach, bool offered) =>
        Assert.Equal(offered, NextSteps.Offered(new NextStep(kind, "w"), manages, mayUseCoach));

    [Fact]
    public void Ask_Coach_puts_tafs_cloud_s_message_in_the_box_else_the_line_and_anything_else_is_a_line()
    {
        Assert.Equal(new NextStepAction.AskCoach("Why was it refused?"), NextSteps.Action(new("ask_coach", "Ask Coach", Message: "Why was it refused?"), null, [], "Refund refused: Stripe signed out"));
        Assert.Equal(new NextStepAction.AskCoach("Refund refused: Stripe signed out"), NextSteps.Action(new("ask_coach", "Ask Coach", Message: "  "), null, [], "Refund refused: Stripe signed out"));
        Assert.IsType<NextStepAction.Line>(NextSteps.Action(new("try_later", "Try again in an hour"), null, [], "f"));
    }
}

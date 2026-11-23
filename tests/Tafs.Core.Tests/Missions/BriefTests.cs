using System.Text.Json;
using Tafs.Core.Missions;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Missions;

/// <summary>Your brief and a mission's log, as the iPad's TafsCloud.Brief, MissionLog and AppModel's brief.</summary>
[Part("home")]
public class BriefTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static Brief Read(string json) => Brief.Read(JsonDocument.Parse(json).RootElement)!;

    const string Sent = """
        {"at":"2026-11-16T07:00:00Z","next_at":"2026-11-16T17:00:00Z",
         "decisions":[
          {"id":"hold:ce_1","question":"Refund order 42, €150?","answers":[
             {"words":"Approve","answer":"approve","recommended":true},{"words":"Refuse","answer":"refuse","decline":true}],
           "why":"It matches the policy.","meanwhile":"The customer waits.","deadline":"by 12:00","urgent":false,
           "later":{"words":"Later"},"ref":{"kind":"hold","id":"ce_1","taf":"Rae"}},
          {"id":"step:h_9","question":"Approve the transfer?","answers":[{"words":"Done","answer":"done"},{"words":"Skip","answer":"skip"}],
           "urgent":true,"later":{"words":"Later"},"now_why":"It expires at 12:00.","ref":{"kind":"step","id":"h_9","taf":"Ana"}},
          {"id":"","question":"No id","answers":[{"words":"Yes"}]},
          {"id":"x","question":"No answers","answers":[]},
          "not a decision"],
         "more":{"count":2,"words":"2 more decisions"},
         "done":[{"mission":{"id":"m_1","name":"Unpaid invoices"},"cases":[{"name":"INV-7","words":"Reminder sent","state":"waiting","next_check":"Mon"}],"value":"€3,400 due back"},
                 {"mission":"Refunds","cases":[]}],
         "next_move":{"words":"Chase the three oldest","start":"Start","dismiss":"Not now","cases":["INV-7"]},
         "rest":{"count":4,"words":"4 need nothing now"},
         "credits":{"words":"€12 of credit left"},
         "labels":[{"id":"l_money","name":"Money","color":"money","count":2,"waiting":1}],
         "words":{"title":"Your brief","decisions":"Decisions · 2","done":"Done since last evening","recommends":"Coach recommends"}}
        """;

    [Fact]
    public void The_brief_reads_as_sent_and_leaves_out_what_it_can_t_draw()
    {
        var b = Read(Sent);
        Assert.Equal(["hold:ce_1", "step:h_9"], b.Decisions.Select(d => d.Id));
        var hold = b.Decisions[0];
        Assert.Equal(("Approve", true), (hold.Recommended!.Words, hold.OffersLater));
        Assert.Equal(new BriefRef("hold", "ce_1", "Rae"), hold.Ref);
        Assert.True(hold.Answers[1].Decline);
        // now_why is why it can't wait; urgent never offers Later, whatever was sent.
        var step = b.Decisions[1];
        Assert.Equal("It expires at 12:00.", step.CantWait);
        Assert.False(step.OffersLater);
        Assert.Null(step.Recommended);

        Assert.Equal(new BriefCounted(2, "2 more decisions"), b.More);
        // Done's mission as {id, name} or as words; a mission with no case is left out.
        var done = Assert.Single(b.Done);
        Assert.Equal(("Unpaid invoices", "m_1", "€3,400 due back"), (done.Mission, done.Log, done.Value));
        Assert.True(done.Cases[0].Waiting);
        Assert.Equal("Chase the three oldest", b.NextMove!.Words);
        Assert.Equal("4 need nothing now", b.Rest!.Words);
        Assert.Equal("€12 of credit left", b.Credits);
        Assert.Equal("l_money", Assert.Single(b.Labels.Labels).Id);
        // The route sends the tab's name as title.
        Assert.Equal(("Your brief", "Decisions · 2"), (b.Words.Tab, b.Words.Decisions));
        Assert.False(b.Empty);
    }

    /// <summary>Coach's pick is drawn only with its why; without one, the answers are equals.</summary>
    [Fact]
    public void A_recommendation_needs_its_why()
    {
        var b = Read("""{"decisions":[{"id":"d","question":"Q","answers":[{"words":"A","recommended":true},{"words":"B"}]}]}""");
        Assert.Null(b.Decisions[0].Recommended);
    }

    /// <summary>No decision, nothing done and no next move is no brief: Needs you stays. Another shape is none at all.</summary>
    [Fact]
    public void An_empty_brief_keeps_Needs_you()
    {
        Assert.True(Read("""{"decisions":[],"done":[],"rest":{"count":3,"words":"3 need nothing now"}}""").Empty);
        Assert.Null(Brief.Read(JsonDocument.Parse("[]").RootElement));
    }

    /// <summary>
    /// A held call opens on its own card with no answer chosen, a step on its own, anything else Needs you: where it goes
    /// never depends on the answer tapped.
    /// </summary>
    [Fact]
    public void Each_decision_goes_where_it_s_answered_and_never_with_an_answer()
    {
        var b = Read(Sent);
        Assert.Equal(new BriefDestination(BriefGoes.Hold, "ce_1"), Brief.Destination(b.Decisions[0]));
        Assert.Equal(new BriefDestination(BriefGoes.Step, "h_9"), Brief.Destination(b.Decisions[1]));
        var connection = b.Decisions[0] with { Ref = new BriefRef("connection", "Gmail", "") };
        Assert.Equal(new BriefDestination(BriefGoes.NeedsYou, null), Brief.Destination(connection));
        Assert.Equal(new BriefDestination(BriefGoes.NeedsYou, null), Brief.Destination(b.Decisions[0] with { Ref = BriefRef.None }));
        // A taf whose checks keep failing opens on its own page, by its tafs cloud id; with none, Needs you.
        Assert.Equal(new BriefDestination(BriefGoes.Taf, "taf-1"), Brief.Destination(b.Decisions[0] with { Ref = new BriefRef("check", "c_1", "taf-1") }));
        Assert.Equal(new BriefDestination(BriefGoes.NeedsYou, null), Brief.Destination(b.Decisions[0] with { Ref = new BriefRef("check", "c_1", "") }));
    }

    /// <summary>Needs you beyond the brief is matched item by item: equal counts of different items still count.</summary>
    [Fact]
    public void Needs_you_beyond_the_brief_is_matched_by_item_never_by_number()
    {
        var shown = Read(Sent).Decisions;
        Assert.Equal(0, Brief.NeedsYouBeyond(["hold:ce_1", "step:h_9"], 0, shown));
        Assert.Equal(1, Brief.NeedsYouBeyond(["hold:ce_1", "hold:ce_2"], 0, shown));
        // What the brief can't carry always counts; the same id of another kind isn't shown.
        Assert.Equal(2, Brief.NeedsYouBeyond(["hold:h_9"], 1, shown));
    }

    /// <summary>The routes: the label as a query, Later's id as one segment keeping its colon, the log's mission and before.</summary>
    [Fact]
    public void The_routes_escape_what_a_path_can_t_hold()
    {
        Assert.Equal("/v1/brief", Brief.Path(null));
        Assert.Equal("/v1/brief?label=l%20money%2F1", Brief.Path("l money/1"));
        Assert.Equal("/v1/brief/decisions/hold:ce_1/later", Brief.LaterPath("hold:ce_1"));
        Assert.Equal("/v1/brief/decisions/step:a%2Fb/later", Brief.LaterPath("step:a/b"));
        Assert.Equal("/v1/missions/m_1/log", MissionLog.Path("m_1"));
        Assert.Equal("/v1/missions/m%2F1/log?before=e%2012", MissionLog.Path("m/1", "e 12"));
    }

    /// <summary>The log: who wrote each entry (a taf by name, Coach, you), a removed taf's entries marked, the next page's cursor.</summary>
    [Fact]
    public void A_mission_s_log_names_who_wrote_each_entry()
    {
        var log = MissionLog.Read(En, JsonDocument.Parse("""
            {"items":[
              {"id":"e3","at":"2026-11-16T09:00:00Z","by":{"kind":"taf","name":"Rae"},"kind":"delivered","kind_words":"delivered",
               "words":"Sent the reminder.","evidence":[{"kind":"email","id":"m1","words":"Email to Acme"},{"kind":"x"}],"value":"€3,400"},
              {"id":"e2","at":"2026-11-16T08:00:00Z","by":{"kind":"lead"},"kind":"note","words":"Took it on."},
              {"id":"e1","at":"2026-11-15T18:00:00Z","by":{"kind":"person"},"kind":"decided","words":"Approved."},
              {"id":"e0","at":"2026-11-15T17:00:00Z","by":{"kind":"taf"},"kind":"stuck","words":"Couldn't log in.","removed":true},
              {"id":"e-","by":{"kind":"taf"},"words":""}],
             "before":"e0"}
            """).RootElement)!;
        Assert.Equal(["e3", "e2", "e1", "e0"], log.Items.Select(i => i.Id));
        Assert.Equal(["Rae", En.Get("coach.title"), En.Get("common.you"), En.Get("needs_you.a_taf")], log.Items.Select(i => i.ByWords));
        Assert.Equal("Email to Acme", Assert.Single(log.Items[0].Evidence).Words);
        Assert.Equal(("€3,400", false), (log.Items[0].Value, log.Items[0].Removed));
        Assert.True(log.Items[3].Removed);
        Assert.Equal("e0", log.Before);
    }

    /// <summary>Coach's chats waiting on their person (`coach_waiting`): tafs cloud sends it only above 0, in its words.</summary>
    [Theory]
    [InlineData("""{"coach_waiting":{"count":2,"words":"Coach waits on you in 2 chats"}}""", "Coach waits on you in 2 chats")]
    [InlineData("""{"coach_waiting":{"count":0,"words":"Coach waits on you in 0 chats"}}""", null)]
    [InlineData("""{"coach_waiting":{"count":1,"words":""}}""", null)]
    [InlineData("""{"coach_waiting":"x"}""", null)]
    [InlineData("""{}""", null)]
    public void Coach_waiting_is_tafs_clouds_line_only_above_zero(string json, string? words) =>
        Assert.Equal(words, Read(json).CoachWaiting?.Words);
}

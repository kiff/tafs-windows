using System.Text.Json;
using Tafs.Core.NeedsYou;

namespace Tafs.Core.Tests.NeedsYou;

/// <summary>Needs you's waiting Coach chats (Home's coach_waiting), as the iPad's NeedsYouView and TafsCloud.CoachWaiting.</summary>
[Part("approvals")]
public class CoachWaitingTests
{
    static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    static readonly JsonElement Home = Json("""
        {"coach_waiting": [
           {"taf": "taf_coach", "session": "s1", "run": "r1", "since": "2026-11-17T08:00:00Z",
            "words": {"line": " Set up refunds: Coach is waiting for your approval since 17 Nov. ", "open": "Open Coach", "set_aside": "Set aside"}},
           {"taf": "taf_coach", "session": "s2", "words": {"line": "Coach is waiting for your answer.", "open": "Open Coach"}},
           {"taf": "taf_coach", "session": "s1", "words": {"line": "again", "open": "Open Coach"}},
           {"taf": "taf_coach", "session": "s3", "words": {"line": "no way in"}},
           {"taf": "taf_coach", "words": {"line": "no chat", "open": "Open Coach"}},
           "not a chat"],
         "coach_waiting_clear": {"words": {"button": "Set all aside", "title": "End 2 waiting Coach chats?", "text": "They stay in Previous conversations.", "yes": "End them", "no": "Keep them"}}}
        """);

    [Fact]
    public void Home_lists_each_waiting_chat_once_with_its_words_and_leaves_out_one_it_cant_draw()
    {
        var listed = CoachWaitings.Read(Home);
        Assert.Equal(["s1", "s2"], listed.Select(c => c.Session));
        Assert.Equal(new CoachWaiting("taf_coach", "s1", "r1", "2026-11-17T08:00:00Z",
            new CoachWaitingWords("Set up refunds: Coach is waiting for your approval since 17 Nov.", "Open Coach", "Set aside")), listed[0]);
        Assert.Equal("", listed[1].Words.SetAside);
        Assert.Empty(CoachWaitings.Read(Json("""{"coach_waiting": null}""")));
        Assert.Empty(CoachWaitings.Read(Json("""{}""")));
    }

    [Fact]
    public void Set_all_aside_shows_for_two_or_more_signed_in_and_only_with_its_button_title_and_yes()
    {
        var clear = CoachWaitings.ReadClear(Home)!;
        Assert.Equal(new CoachWaitingClear("Set all aside", "End 2 waiting Coach chats?", "They stay in Previous conversations.", "End them", "Keep them"), clear);
        Assert.Null(CoachWaitings.ReadClear(Json("""{"coach_waiting_clear": {"words": {"button": "Set all aside", "title": "End?"}}}""")));
        Assert.Null(CoachWaitings.ReadClear(Json("""{}""")));
        Assert.Same(clear, CoachWaitings.SetAllAside(clear, 2, signedIn: true));
        Assert.Null(CoachWaitings.SetAllAside(clear, 1, signedIn: true));
        Assert.Null(CoachWaitings.SetAllAside(clear, 2, signedIn: false));
    }

    [Fact]
    public void The_chat_open_in_Coach_and_those_being_set_aside_are_not_shown_and_set_aside_needs_its_words()
    {
        var listed = CoachWaitings.Read(Home);
        Assert.Equal(["s2"], CoachWaitings.Shown(listed, "s1", new HashSet<string>()).Select(c => c.Session));
        Assert.Equal(["s1"], CoachWaitings.Shown(listed, null, new HashSet<string> { "s2" }).Select(c => c.Session));
        Assert.True(CoachWaitings.CanSetAside(listed[0], signedIn: true));
        Assert.False(CoachWaitings.CanSetAside(listed[1], signedIn: true));
        Assert.False(CoachWaitings.CanSetAside(listed[0], signedIn: false));
    }

    [Fact]
    public void Setting_aside_posts_to_one_chat_or_all_and_reads_what_ended_and_where_it_is()
    {
        Assert.Equal("/v1/home/coach-waiting/s%2F1/set-aside", CoachWaitings.SetAsidePath("s/1"));
        Assert.Equal("/v1/home/coach-waiting/set-aside", CoachWaitings.SetAsidePath(null));
        var (ended, done) = CoachWaitings.ReadSetAside(Json("""{"set_aside": ["s1", "s2"], "withdrawn": 0, "words": {"done": "2 chats set aside. You'll find them in Previous conversations."}}"""));
        Assert.Equal(["s1", "s2"], ended);
        Assert.Equal("2 chats set aside. You'll find them in Previous conversations.", done);
        Assert.Equal(([], null), (CoachWaitings.ReadSetAside(Json("""{"set_aside": []}""")).Ended.ToArray(), CoachWaitings.ReadSetAside(Json("{}")).Done));
    }
}

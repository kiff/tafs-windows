using System.Text;
using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>A taf's work (<c>GET /v1/tafs/{id}/work</c>), as the iPad's TafWork, WorkPanel and HealthLines read it.</summary>
[Part("record")]
public class TafWorkTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    const string Answer = """
        {"summary": {"status": "idle", "line": "Its check couldn't read Gmail", "needs_you": 1},
         "health": {"level": "failing", "lines": ["Its check couldn't read Gmail, so nothing ran.", "  ", "Out of credits."],
                    "fixes": [{"line": 0, "kind": "coach", "words": "Fix it with Coach", "say": "Gmail's check fails"},
                              {"line": 0, "kind": "coach", "words": "No say"},
                              {"line": 1, "kind": "credits", "words": "Add credits"},
                              {"line": 1, "kind": "teleport", "words": "?"}]},
         "oldest_waiting": {"since": "2026-11-16T08:00:00Z", "words": "Oldest case waiting 5 h", "late": true},
         "promise": {"hours": 48},
         "requests": [
           {"id": "r1", "at": "2026-11-16T09:00:00Z", "state": "done", "line": "Answered Ana's refund", "credits": {"credits": 34, "counted": true, "words": "34 credits"},
            "deliveries": [{"place": "door_reply", "state": "not_sent", "line": "Not sent: Gmail refused", "send_again": true}]},
           {"id": "r2", "at": "2026-11-16T11:00:00Z", "state": "needs_you", "line": "Waiting for you", "deliveries": [{"place": "action", "key": "grt_1:notion.append", "state": "retrying", "line": "Retrying Notion"}]},
           {"state": "done"}
         ],
         "skipped": [{"due": "2026-11-16T10:00:00Z", "why": "busy", "words": "10:00 skipped: still working"}]}
        """;

    static TafWork Work() => TafWork.Read(JsonDocument.Parse(Answer).RootElement)!;

    [Fact]
    public void It_reads_the_line_health_oldest_case_promise_and_rows_newest_first()
    {
        var work = Work();
        Assert.Equal("Its check couldn't read Gmail", work.Line);
        Assert.Equal(("Oldest case waiting 5 h", true), (work.Oldest!.Words, work.Oldest.Late));
        Assert.Equal(48, work.PromiseHours);
        Assert.Equal(2, work.Requests.Count);
        Assert.Equal(["r2", "skipped", "r1"], work.Rows.Select(r => r.Request?.Id ?? "skipped"));
        Assert.Equal("34 credits", work.Requests[0].CreditsLine);
        Assert.Equal(["r1"], work.NotSent.Select(r => r.Id));
        Assert.Equal(("door_reply", "grt_1:notion.append"), (work.Requests[0].Deliveries[0].SendPlace, work.Requests[1].Deliveries[0].SendPlace));
    }

    /// <summary>The health says its lines and the fixes the app can act on; a work line that's its first reason is said once.</summary>
    [Fact]
    public void Its_health_draws_its_lines_and_the_fixes_it_knows()
    {
        var health = Work().Health!;
        Assert.True(health.NeedsAttention && health.Failing);
        Assert.Equal(["Its check couldn't read Gmail, so nothing ran.", "Out of credits."], health.Lines);
        Assert.Equal(["coach"], health.FixesFor(0).Select(f => f.Kind));
        Assert.Equal(["credits"], health.FixesFor(1).Select(f => f.Kind));
        Assert.True(health.Repeats(Work().Line));
        Assert.False(TafHealth.Healthy.NeedsAttention);
        Assert.Equal(TafHealth.Healthy, TafHealth.Read(JsonDocument.Parse("[]").RootElement));
    }

    /// <summary>Run now: a run started says nothing; one that started nothing says tafs cloud's words, as they are.</summary>
    [Fact]
    public void Run_now_says_tafs_cloud_s_words_only_when_nothing_ran()
    {
        Assert.Null(TafRunNow.Words(JsonDocument.Parse("""{"session": "s1"}""").RootElement));
        Assert.Equal("Checked for new work: nothing new.",
            TafRunNow.Words(JsonDocument.Parse("""{"session": null, "outcome": "quiet", "words": "Checked for new work: nothing new."}""").RootElement));
    }

    [Fact]
    public void Its_routes_and_the_promise()
    {
        Assert.Equal("/v1/tafs/t%2F1/work", TafWork.Path("t/1"));
        Assert.Equal("/v1/tafs/t1/requests/r1/deliveries/grt_1%3Anotion.append/send", TafWork.SendAgainPath("t1", "r1", "grt_1:notion.append"));
        Assert.Equal("""{"hours":24}""", Encoding.UTF8.GetString(TafWork.PromiseBody(24)));
        Assert.Equal("""{"hours":null}""", Encoding.UTF8.GetString(TafWork.PromiseBody(null)));
        Assert.Equal(("Not set", "1 day", "7 days", "8 hours"), (TafWork.PromiseWords(En, null), TafWork.PromiseWords(En, 24), TafWork.PromiseWords(En, 168), TafWork.PromiseWords(En, 8)));
        Assert.Null(TafWork.Read(JsonDocument.Parse("[]").RootElement));
    }
}

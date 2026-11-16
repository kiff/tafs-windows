using System.Text.Json;
using Tafs.Core.Taf;

namespace Tafs.Core.Tests.Taf;

/// <summary>The runs a taf kept, page by page, and a run's own page (S19), as the iPad's RetainedRuns and KnobRequestDetail.</summary>
[Part("record")]
public class TafRunPagesTests
{
    [Fact]
    public void A_page_of_kept_runs_puts_firings_that_started_none_in_their_place()
    {
        var page = RunPage.Read(JsonDocument.Parse("""
            {"title": "Its runs", "more_title": "See all its runs", "next_cursor": "c+2",
             "requests": [{"id": "r2", "at": "2026-11-16T11:00:00Z", "state": "done", "line": "Answered Ana", "deliveries": []},
                          {"id": "r1", "at": "2026-11-16T09:00:00Z", "state": "failed", "line": "Couldn't read Gmail", "deliveries": []}],
             "skipped": [{"due": "2026-11-16T10:00:00Z", "why": "check_failed", "words": "Run now: its check couldn't read"}]}
            """).RootElement)!;
        Assert.Equal(("Its runs", "See all its runs", "c+2", (string?)null), (page.Title, page.MoreTitle, page.NextCursor, page.EndWords));
        Assert.Equal(["r2", "skipped", "r1"], page.Rows(first: true).Select(r => r.Request?.Id ?? "skipped"));
        Assert.Equal(["r2", "r1"], page.Rows(first: false).Select(r => r.Request!.Id));
        Assert.Equal("/v1/tafs/t1/work/runs?limit=20&cursor=c%2B2", RunPage.Path("t1", 20, "c+2"));
        Assert.Equal("/v1/tafs/t1/work/runs?limit=3", RunPage.Path("t1", 3));
    }

    [Fact]
    public void A_run_s_own_page_has_where_it_came_from_its_steps_and_its_reply()
    {
        var run = RunDetail.Read(JsonDocument.Parse("""
            {"from": "By email", "line": "Answered Ana's refund",
             "steps": [{"label": "Received", "line": "09:00", "state": "done"}, {"label": "Refund a payment", "state": "done"},
                       {"label": "Waiting for Stripe", "state": "waiting", "waits_on": "app"}, {"state": "next"}],
             "deliveries": [{"place": "door_reply", "state": "not_sent", "line": "Not sent: Gmail refused", "send_again": true}]}
            """).RootElement)!;
        Assert.Equal(("By email", "Answered Ana's refund"), (run.From, run.Line));
        Assert.Equal(["Received", "Refund a payment", "Waiting for Stripe"], run.Steps.Select(s => s.Label));
        Assert.True(run.Steps[2].WaitsOnApp);
        Assert.Equal("door_reply", Assert.Single(run.Deliveries).SendPlace);
        Assert.Equal("/v1/tafs/t1/requests/r%2F1", RunDetail.Path("t1", "r/1"));
    }
}

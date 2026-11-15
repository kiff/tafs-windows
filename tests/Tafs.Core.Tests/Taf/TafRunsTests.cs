using System.Text.Json;
using Tafs.Core.Contracts;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

[Part("record")]
public class TafRunsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static JsonElement Answer(string file) => ContractFile.Answer(SharedFiles.Contract(file));

    static readonly IReadOnlyList<TafRun> Runs = TafRuns.Read(Answer("taf-runs.json"));

    [Fact]
    public void A_taf_s_runs_read_newest_first_with_why_one_stopped()
    {
        Assert.Equal(new[] { "sess3", "sess1" }, Runs.Select(r => r.Session));
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), Runs[0].At);
        Assert.Null(Runs[0].Error);
        Assert.Equal("The taf stopped before it finished: the app didn't answer.", Runs[1].Error);
        Assert.Equal("sess3", TafRuns.Latest(Runs)?.Session);
    }

    [Fact]
    public void A_run_s_report_reads_ended_running_and_terminated()
    {
        Assert.Equal(new RunReport("sess3", "idle", "Evidence went on the disputes; one waits for you.",
            "From the Record — done: 5 · waiting for you: 1 · refused: 2"), TafRuns.ReadReport(Answer("taf-run-report.json")));
        Assert.Equal(new RunReport("sess3", "running", "", null), TafRuns.ReadReport(Answer("taf-run-report-running.json")));
        Assert.Equal(new RunReport("sess1", "terminated", "", null), TafRuns.ReadReport(Answer("taf-run-report-terminated.json")));
        Assert.Null(TafRuns.ReadReport(JsonDocument.Parse("""{"session": "s"}""").RootElement));
    }

    [Fact]
    public void Last_result_is_the_report_s_first_line_else_why_it_stopped_else_when_it_ran()
    {
        var utc = TimeZoneInfo.Utc;
        Assert.Equal("Evidence went on the disputes; one waits for you.",
            TafRuns.LastResult(En, Runs, TafRuns.ReadReport(Answer("taf-run-report.json")), utc));
        Assert.Equal("The taf stopped before it finished: the app didn't answer.",
            TafRuns.LastResult(En, Runs, TafRuns.ReadReport(Answer("taf-run-report-terminated.json")), utc));
        Assert.Equal("Last ran at 12:00", TafRuns.LastResult(En, Runs, TafRuns.ReadReport(Answer("taf-run-report-running.json")), utc));
        Assert.Equal("Nothing yet", TafRuns.LastResult(En, [], null, utc));
        Assert.Equal("–", TafRuns.LastResult(En, null, null, utc));
    }

    [Fact]
    public void Its_Markdown_is_dropped_and_blank_lines_skipped() =>
        Assert.Equal("Done: 3 refunds", TafRuns.LastResult(En, Runs, new RunReport("sess3", "idle", "\n  ## **Done**: 3 refunds\nmore", null), TimeZoneInfo.Utc));

    [Fact]
    public void What_it_can_t_read_reads_as_none()
    {
        var runs = TafRuns.Read(JsonDocument.Parse("""{"runs": [{"at": "soon"}, 3, {"session": "s", "error": 7}]}""").RootElement);
        Assert.Equal(new[] { new TafRun(null, null, null), new TafRun("s", null, null) }, runs);
        Assert.Null(TafRuns.Latest([runs[0]]));
        Assert.Empty(TafRuns.Read(JsonDocument.Parse("[]").RootElement));
    }
}

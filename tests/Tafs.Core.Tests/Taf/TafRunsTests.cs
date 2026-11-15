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

    static readonly DateTimeOffset LastRun = new(2026, 10, 16, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The iPad's lastResult: the report's first line, else its error; else Home's last run; else "Nothing yet".</summary>
    [Fact]
    public void Last_result_is_the_report_s_first_line_else_its_error_else_when_it_last_ran()
    {
        var utc = TimeZoneInfo.Utc;
        Assert.Equal("Evidence went on the disputes; one waits for you.",
            TafRuns.LastResult(En, new LatestRun("sess3", Text: "Evidence went on the disputes; one waits for you."), LastRun, utc));
        Assert.Equal("Stopped", TafRuns.LastResult(En, new LatestRun(Error: "Stopped"), LastRun, utc));
        Assert.Equal("Last ran at 09:30", TafRuns.LastResult(En, new LatestRun("sess3", Status: "running"), LastRun, utc));
        Assert.Equal("Last ran at 09:30", TafRuns.LastResult(En, null, LastRun, utc));
        Assert.Equal("Nothing yet", TafRuns.LastResult(En, null, null, utc));
    }

    [Fact]
    public void Its_Markdown_is_dropped_and_blank_lines_skipped() =>
        Assert.Equal("Done: 3 refunds", TafRuns.LastResult(En, new LatestRun(Text: "\n  ## **Done**: 3 refunds\nmore"), null, TimeZoneInfo.Utc));

    /// <summary>tafs cloud's answers by path, from the contracts; a path not given wasn't reached.</summary>
    static Func<string, Task<CloudRead>> Cloud(List<string> asked, params (string Path, string File)[] answers) => path =>
    {
        asked.Add(path);
        return Task.FromResult(answers.FirstOrDefault(a => a.Path == path).File is { } file ? CloudRead.Of(Answer(file)) : CloudRead.Unreached(En));
    };

    [Fact]
    public async Task Its_latest_run_is_the_report_of_the_newest_that_started()
    {
        var asked = new List<string>();
        var read = Cloud(asked, ("/v1/tafs/taf-1/runs", "taf-runs.json"), ("/v1/tafs/taf-1/runs/sess3", "taf-run-report.json"));

        var latest = await TafRuns.LoadReportAsync(En, "taf-1", read);

        Assert.Equal(new LatestRun("sess3", Runs[0].At, "idle", "Evidence went on the disputes; one waits for you."), latest);
        Assert.Equal(new[] { "/v1/tafs/taf-1/runs", "/v1/tafs/taf-1/runs/sess3" }, asked);
    }

    /// <summary>With no run that started, the first run's time and error, and no report is asked for.</summary>
    [Fact]
    public async Task With_no_run_started_it_keeps_the_first_run_s_time_and_error()
    {
        var asked = new List<string>();
        var none = JsonDocument.Parse("""{"runs": [{"session": null, "at": "2026-10-16T08:00:00Z", "error": "No app connected"}]}""").RootElement;
        var latest = await TafRuns.LoadReportAsync(En, "t", path => { asked.Add(path); return Task.FromResult(CloudRead.Of(none)); });

        Assert.Equal(new LatestRun(At: new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero), Error: "No app connected"), latest);
        Assert.Equal("No app connected", TafRuns.LastResult(En, latest, LastRun, TimeZoneInfo.Utc));
        Assert.Single(asked);
    }

    /// <summary>Runs or a report that can't be read say so, with why: tafs cloud's words when it sent them.</summary>
    [Fact]
    public async Task What_can_t_be_read_says_its_runs_couldn_t_be_loaded_and_why()
    {
        var asked = new List<string>();
        Assert.Equal("Its runs couldn't be loaded. It didn't reach the server. Try again once you're online.",
            (await TafRuns.LoadReportAsync(En, "taf-1", Cloud(asked))).Error);
        Assert.Equal(new[] { "/v1/tafs/taf-1/runs" }, asked);

        var refused = await TafRuns.LoadReportAsync(En, "taf-1", path => Task.FromResult(path.EndsWith("/runs")
            ? CloudRead.Of(Answer("taf-runs.json"))
            : CloudRead.Refused(En, 503, """{"error": "busy", "words": "tafs cloud is busy. Try again in a minute."}""")));
        Assert.Equal("Its runs couldn't be loaded. tafs cloud is busy. Try again in a minute.", refused.Error);
    }

    [Theory]
    [InlineData("""{"error": "taf not found"}""", 404, "taf not found")]
    [InlineData("not json", 502, "tafs cloud answered 502")]
    [InlineData("", 500, "tafs cloud answered 500")]
    public void A_refusal_says_tafs_cloud_s_words_else_its_error_else_its_status(string body, int status, string why) =>
        Assert.Equal(why, CloudRead.Refused(En, status, body).Why);

    [Fact]
    public void A_taf_s_id_and_a_session_are_escaped_in_the_path() =>
        Assert.Equal("/v1/tafs/a%2Fb%3F/runs/s%23", TafRuns.ReportPath("a/b?", "s#"));

    [Fact]
    public void What_it_can_t_read_reads_as_none()
    {
        var runs = TafRuns.Read(JsonDocument.Parse("""{"runs": [{"at": "soon"}, 3, {"session": "s", "error": 7}]}""").RootElement);
        Assert.Equal(new[] { new TafRun(null, null, null), new TafRun("s", null, null) }, runs);
        Assert.Null(TafRuns.Latest([runs[0]]));
        Assert.Empty(TafRuns.Read(JsonDocument.Parse("[]").RootElement));
    }
}

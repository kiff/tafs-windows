using System.Globalization;
using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>One of a taf's runs, newest first: its session (none for one that never started), when, and why it stopped.</summary>
public sealed record TafRun(string? Session, DateTimeOffset? At, string? Error);

/// <summary>One run's report: its status (<c>idle</c>, <c>running</c>, <c>terminated</c>…), its last answer, and its count from the Record.</summary>
public sealed record RunReport(string Session, string Status, string Text, string? Counted);

/// <summary>
/// A taf's runs (<c>GET /v1/tafs/{id}/runs</c>, contracts/taf-runs.json) and one run's report (<c>GET /v1/tafs/{id}/runs/{session}</c>,
/// taf-run-report*.json), for the Overview's Last result, as the iPad's loadReport and lastResult.
/// </summary>
public static class TafRuns
{
    public static IReadOnlyList<TafRun> Read(JsonElement answer) =>
        Lenient.Array(answer, "runs").Where(r => r.ValueKind == JsonValueKind.Object).Select(r => new TafRun(
            Lenient.Text(r, "session"),
            DateTimeOffset.TryParse(Lenient.Text(r, "at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null,
            Lenient.Text(r, "error"))).ToList();

    /// <summary>A report with no session or no report reads as none.</summary>
    public static RunReport? ReadReport(JsonElement answer) =>
        Lenient.Text(answer, "session") is { } session && Lenient.Field(answer, "report") is { ValueKind: JsonValueKind.Object } r
            ? new RunReport(session, Lenient.Text(r, "status") ?? "", Lenient.Text(r, "text") ?? "", Lenient.Text(r, "counted"))
            : null;

    public static string RunsPath(string taf) => $"/v1/tafs/{CloudRead.PathPart(taf)}/runs";

    public static string ReportPath(string taf, string session) => $"{RunsPath(taf)}/{CloudRead.PathPart(session)}";

    /// <summary>
    /// A taf's latest run, as the iPad's <c>AppModel.loadReport</c>, read through <paramref name="read"/>: its runs, then the
    /// report of the newest that started. With none started, the first run's time and error. Runs or a report that can't be
    /// read say so, "Its runs couldn't be loaded." and why.
    /// </summary>
    public static async Task<LatestRun> LoadReportAsync(WordBook w, string taf, Func<string, Task<CloudRead>> read)
    {
        var listed = await read(RunsPath(taf));
        if (listed.Answer is not { } answer) return NotLoaded(w, listed);
        var runs = Read(answer);
        if (Latest(runs) is not { Session: { } session } latest) return new(At: runs.FirstOrDefault()?.At, Error: runs.FirstOrDefault()?.Error);
        var reported = await read(ReportPath(taf, session));
        if (reported.Answer is not { } r) return NotLoaded(w, reported);
        var report = ReadReport(r);
        return new(session, latest.At, report?.Status, report?.Text ?? "");
    }

    static LatestRun NotLoaded(WordBook w, CloudRead failed) => new(Error: w.Get("report.runs_not_loaded", ("why", failed.Why ?? "")));

    /// <summary>The run whose report the Overview reads: the newest one that started (has a session).</summary>
    public static TafRun? Latest(IReadOnlyList<TafRun> runs) => runs.FirstOrDefault(r => r.Session is not null);

    /// <summary>
    /// The Overview's Last result, as the iPad's <c>OverviewView.lastResult</c> (ShareViews.swift): the first line of its latest
    /// run's report (its Markdown's <c>**</c> and <c>#</c> dropped), else that run's error; else when it last ran, from Home's
    /// <c>last_run</c>; else "Nothing yet". Before its report is read, <paramref name="report"/> is none.
    /// </summary>
    public static string LastResult(WordBook w, LatestRun? report, DateTimeOffset? lastRun, TimeZoneInfo zone)
    {
        if (report is not null)
        {
            var first = report.Text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            if (first is not null) return first.Replace("**", "").Replace("#", "").Trim();
            if (report.Error is { } error) return error;
        }
        return lastRun is { } at
            ? w.Get("people.last_ran", ("time", TimeZoneInfo.ConvertTime(at, zone).ToString("HH:mm", CultureInfo.InvariantCulture)))
            : w.Get("overview.nothing_yet");
    }
}

/// <summary>A taf's latest run as the iPad's <c>AppModel.RunReport</c> keeps it: its session, time, status, report and error.</summary>
public sealed record LatestRun(string? Session = null, DateTimeOffset? At = null, string? Status = null, string Text = "", string? Error = null);

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

    public static string RunsPath(string taf) => $"/v1/tafs/{Uri.EscapeDataString(taf)}/runs";

    public static string ReportPath(string taf, string session) => $"{RunsPath(taf)}/{Uri.EscapeDataString(session)}";

    /// <summary>
    /// A taf's Last result, read through <paramref name="read"/> (a path to tafs cloud's answer, none when it can't be read):
    /// its runs, then the report of the newest that started. Runs that can't be read show "–"; a report that can't be read
    /// leaves why it stopped, else when it ran.
    /// </summary>
    public static async Task<string> LastResultAsync(WordBook w, string taf, Func<string, Task<JsonElement?>> read, TimeZoneInfo zone)
    {
        if (await read(RunsPath(taf)) is not { } answer) return LastResult(w, null, null, zone);
        var runs = Read(answer);
        var report = Latest(runs) is { Session: { } session } && await read(ReportPath(taf, session)) is { } r ? ReadReport(r) : null;
        return LastResult(w, runs, report, zone);
    }

    /// <summary>The run whose report the Overview reads: the newest one that started (has a session).</summary>
    public static TafRun? Latest(IReadOnlyList<TafRun> runs) => runs.FirstOrDefault(r => r.Session is not null);

    /// <summary>
    /// The Overview's Last result, as the iPad's lastResult: the first line of the last run's report (its Markdown's
    /// <c>**</c> and <c>#</c> dropped); a report carries no error, so for a run that stopped with no answer, why, from the runs
    /// list; else when it last ran; else "Nothing yet". A taf tafs cloud doesn't run (the sample's) shows "–".
    /// </summary>
    public static string LastResult(WordBook w, IReadOnlyList<TafRun>? runs, RunReport? report, TimeZoneInfo zone)
    {
        if (runs is null) return "–";
        var first = report?.Text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (first is not null) return first.Replace("**", "").Replace("#", "").Trim();
        var run = (report is null ? null : runs.FirstOrDefault(r => r.Session == report.Session)) ?? runs.FirstOrDefault();
        if (run?.Error is { Length: > 0 } error) return error;
        if (run?.At is { } at) return w.Get("people.last_ran", ("time", TimeZoneInfo.ConvertTime(at, zone).ToString("HH:mm", CultureInfo.InvariantCulture)));
        return w.Get("overview.nothing_yet");
    }
}

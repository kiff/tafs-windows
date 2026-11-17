using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>How a report went, as the iPad's <c>AppModel.ReportOutcome</c>: kept, kept before, or not sent with why.</summary>
public abstract record ReportOutcome
{
    public sealed record Sent : ReportOutcome;
    public sealed record Already : ReportOutcome;
    public sealed record Failed(string Why) : ReportOutcome;
}

/// <summary>
/// Reporting an answer Coach wrote (Google Play's rule for content an AI generates), as the iPad's
/// <c>ReportAnswerSheet</c>, <c>AppModel.canReport</c>/<c>reportAnswer</c> and <c>TafsCloud.ReportReason</c>/<c>reportAnswer</c>:
/// a reason, an optional note, sent to tafs cloud with the answer's chat so a person at KIFF reads it.
/// </summary>
public static class AnswerReport
{
    /// <summary>The reasons, in the iPad's order (<c>ReportReason.allCases</c>), as tafs cloud takes them.</summary>
    public static readonly IReadOnlyList<string> Reasons = ["offensive", "harmful", "wrong", "other"];

    /// <summary>The longest note tafs cloud takes, counted in characters as a person sees them, as Swift's <c>count</c>.</summary>
    public const int NoteLimit = 1000;

    public static string Title(WordBook w, string reason) => w.Get($"report.reason.{reason}");

    /// <summary>A note past the limit: Send is off and the red line says so.</summary>
    public static bool TooLong(string note) => new StringInfo(note).LengthInTextElements > NoteLimit;

    /// <summary>
    /// Whether an answer can be reported, as <c>canReport</c>: Coach's, never the person's own, a message rather than a run's
    /// start, with words, and one tafs cloud has an id for, for a KIFF account.
    /// </summary>
    public static bool Can(ChatMessage m, bool kiff) =>
        m.From == ChatFrom.Taf && !m.Kickoff && m.Text.Length > 0 && kiff && m.ServerId is not null;

    /// <summary>
    /// <c>POST /v1/tafs/{id}/messages/{message}/report</c> with <c>reason</c>, <c>note</c> and <c>session</c>, as the iPad's:
    /// the note trimmed, and left out when blank, as Swift leaves out a nil.
    /// </summary>
    public static (string Path, byte[] Body) Request(string taf, ChatMessage m, string reason, string note)
    {
        var body = new JsonObject { ["reason"] = reason };
        var trimmed = note.Trim();
        if (trimmed.Length > 0) body["note"] = trimmed;
        if (m.Session.Length > 0) body["session"] = m.Session;
        return ($"/v1/tafs/{CloudRead.PathPart(taf)}/messages/{CloudRead.PathPart(m.ServerId ?? "")}/report",
            System.Text.Encoding.UTF8.GetBytes(body.ToJsonString()));
    }

    /// <summary>
    /// The report sent and what tafs cloud's answer comes to, as <c>reportAnswer</c>: no Coach or no id can't be reported
    /// from here; an answer that comes after the person signed out (<paramref name="send"/> gives none) says so.
    /// </summary>
    public static async Task<ReportOutcome> SendAsync(WordBook w, Func<string, byte[], Task<CloudRead?>> send, string? coach, ChatMessage m,
        string reason, string note)
    {
        if (coach is null || m.ServerId is null) return new ReportOutcome.Failed(w.Get("report.cant_from_here"));
        var (path, body) = Request(coach, m, reason, note);
        return await send(path, body) is { } r ? Outcome(w, r) : new ReportOutcome.Failed(w.Get("common.you_signed_out"));
    }

    /// <summary>
    /// <c>{"report": {"id", "at", "new"}}</c>: new is sent, else reported already. A refusal with tafs cloud's <c>words</c>
    /// says them; a 404, that the answer isn't on tafs cloud any more; anything else, in the app's words, never tafs
    /// cloud's codes.
    /// </summary>
    public static ReportOutcome Outcome(WordBook w, CloudRead r)
    {
        if (r.Answer is { } a)
            return a.ValueKind == JsonValueKind.Object && a.TryGetProperty("report", out var report) && report.ValueKind == JsonValueKind.Object
                && Text(report, "id") && Text(report, "at") && report.TryGetProperty("new", out var n) && n.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? n.GetBoolean() ? new ReportOutcome.Sent() : new ReportOutcome.Already()
                : new ReportOutcome.Failed(w.Get("kiff.did_not_reach_kiff_try"));
        if (r.Words is { } words) return new ReportOutcome.Failed(words);
        if (r.Status == 404) return new ReportOutcome.Failed(w.Get("needs_you.answer_gone"));
        return new ReportOutcome.Failed(w.Get("kiff.did_not_reach_kiff_try"));
    }

    static bool Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String;
}

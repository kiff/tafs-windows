using System.Globalization;
using System.Text.Json;

namespace Tafs.Core.Taf;

/// <summary>What Run now answered: a run started says nothing; one that started nothing says tafs cloud's words.</summary>
public static class TafRunNow
{
    public static string? Words(JsonElement answer) =>
        answer.ValueKind == JsonValueKind.Object && Lenient.Text(answer, "session") is null && Lenient.Text(answer, "words")?.Trim() is { Length: > 0 } words
            ? words : null;
}

/// <summary>A way to fix what one health line says: Coach opened with <c>say</c> in its box, or Plan &amp; usage (credits).</summary>
public sealed record HealthFix(int Line, string Kind, string Words, string? Say);

/// <summary>
/// A taf's health as tafs cloud reads it (kiff/tafs docs/reliability.md, "Its health"): its level, each reason as a line, and
/// the fixes tafs cloud decides. The app decides nothing: an unknown level is drawn as its lines, a fix it doesn't know is none.
/// </summary>
public sealed record TafHealth(string Level, IReadOnlyList<string> Lines, IReadOnlyList<HealthFix> Fixes)
{
    public static readonly TafHealth Healthy = new("healthy", [], []);

    /// <summary>Something to show: a level other than healthy, with a line to say why.</summary>
    public bool NeedsAttention => Level != "healthy" && Lines.Count > 0;

    public bool Failing => Level == "failing";

    /// <summary>Whether a work line is this health's first reason, as an idle taf's work says it: said once, below.</summary>
    public bool Repeats(string workLine) =>
        NeedsAttention && workLine.Trim() is { Length: > 0 } line && Lines[0].StartsWith(line, StringComparison.Ordinal);

    /// <summary>The fixes for one line, of the kinds the app can act on, in tafs cloud's order.</summary>
    public IReadOnlyList<HealthFix> FixesFor(int line) =>
        Fixes.Where(f => f.Line == line && f.Kind is "coach" or "credits" && f.Words.Length > 0 && (f.Kind != "coach" || !string.IsNullOrEmpty(f.Say))).ToList();

    /// <summary>Never fails the read it's on: one that isn't a health reads as healthy, with nothing to draw.</summary>
    public static TafHealth Read(JsonElement? h)
    {
        if (h is not { ValueKind: JsonValueKind.Object } o) return Healthy;
        var lines = Lenient.Array(o, "lines").Where(l => l.ValueKind == JsonValueKind.String).Select(l => l.GetString()!)
            .Where(l => l.Trim().Length > 0).ToList();
        var fixes = Lenient.Array(o, "fixes").Where(f => f.ValueKind == JsonValueKind.Object)
            .Select(f => new HealthFix(Lenient.Int(f, "line") ?? -1, Lenient.Text(f, "kind") ?? "", Lenient.Text(f, "words") ?? "", Lenient.Text(f, "say"))).ToList();
        return new(Lenient.Text(o, "level") ?? "healthy", lines, fixes);
    }
}

/// <summary>Where a request's result went, and whether it can be sent again (a reply given up on, or a place of the taf's own).</summary>
public sealed record WorkDelivery(string Place, string? Key, string State, string Line, bool SendAgain)
{
    /// <summary>What Send again names in its route: the door's reply, or the place's own key.</summary>
    public string SendPlace => Place == "door_reply" ? "door_reply" : Key ?? "door_reply";
}

/// <summary>One request: done, working, retrying, needs_you, waiting, stopped or failed, in tafs cloud's words, and where it went.</summary>
public sealed record WorkRequest(string Id, DateTimeOffset At, string State, string Line, IReadOnlyList<WorkDelivery> Deliveries, string? CreditsLine);

/// <summary>A scheduled run that was skipped: not a failure, nothing ran.</summary>
public sealed record WorkSkipped(DateTimeOffset Due, string Why, string Words);

/// <summary>Its oldest waiting case, in tafs cloud's words, and whether it's past the owner's promise.</summary>
public sealed record OldestWaiting(string Words, bool Late);

/// <summary>A row of the work, newest first: a request, or a skipped firing.</summary>
public sealed record TafWorkRow(DateTimeOffset At, WorkRequest? Request, WorkSkipped? Skipped);

/// <summary>
/// Where a taf's work stands, as tafs cloud says it (<c>GET /v1/tafs/{id}/work</c>, docs/reliability.md), as the iPad's
/// <c>TafsCloud.TafWork</c>: its line, its health, its oldest waiting case, the owner's promise, and its recent requests and
/// skipped firings. Every line and time is tafs cloud's, in the account's zone: the app formats none.
/// </summary>
public sealed record TafWork(string Line, TafHealth? Health, OldestWaiting? Oldest, int? PromiseHours, IReadOnlyList<WorkRequest> Requests,
    IReadOnlyList<WorkSkipped> Skipped)
{
    public static string Path(string taf) => "/v1/tafs/" + CloudRead.PathPart(taf) + "/work";

    /// <summary><c>PUT</c>: the owner's promise, in whole hours, or none to clear it.</summary>
    public static string PromisePath(string taf) => "/v1/tafs/" + CloudRead.PathPart(taf) + "/promise";

    /// <summary><c>POST</c>: Send again, a run's reply or place by its route's name, the place key (<c>grant:action</c>) URL-encoded whole.</summary>
    public static string SendAgainPath(string taf, string run, string place) =>
        $"/v1/tafs/{CloudRead.PathPart(taf)}/requests/{CloudRead.PathPart(run)}/deliveries/{CloudRead.PathPart(place).Replace(":", "%3A", StringComparison.Ordinal)}/send";

    /// <summary><c>{"hours": n}</c>, or <c>{"hours": null}</c> to clear it.</summary>
    public static byte[] PromiseBody(int? hours) => JsonSerializer.SerializeToUtf8Bytes(new { hours });

    /// <summary>The hours a case may wait, as the owner picks them; tafs cloud checks the range (1 to 168).</summary>
    public static readonly int[] PromiseChoices = [1, 2, 4, 8, 24, 48, 168];

    /// <summary>A promise in words: whole days as days ("1 day", "7 days"), anything else in hours; none set, "Not set".</summary>
    public static string PromiseWords(Words.WordBook w, int? hours) =>
        hours is not { } h ? w.Get("work.answer_within_none")
        : h >= 24 && h % 24 == 0 ? w.Count("work.answer_within_days", h / 24) : w.Count("work.answer_within_hours", h);

    /// <summary>The requests and skipped firings, each in its own place by time, newest first.</summary>
    public IReadOnlyList<TafWorkRow> Rows =>
        Requests.Select(r => new TafWorkRow(r.At, r, null)).Concat(Skipped.Select(s => new TafWorkRow(s.Due, null, s)))
            .OrderByDescending(r => r.At).ToList();

    /// <summary>The replies given up on after their tries: Send again, for whoever manages the taf.</summary>
    public IEnumerable<WorkRequest> NotSent => Requests.Where(r => r.Deliveries.Any(d => d.SendAgain));

    /// <summary>tafs cloud's answer; a field it can't read is none, never the read. None for one that isn't an object.</summary>
    public static TafWork? Read(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) return null;
        var summary = Lenient.Field(a, "summary") is { ValueKind: JsonValueKind.Object } s ? Lenient.Text(s, "line") ?? "" : "";
        var oldest = Lenient.Field(a, "oldest_waiting") is { ValueKind: JsonValueKind.Object } o && Lenient.Text(o, "words")?.Trim() is { Length: > 0 } words
            ? new OldestWaiting(words, Lenient.Bool(o, "late")) : null;
        var promise = Lenient.Field(a, "promise") is { ValueKind: JsonValueKind.Object } p && Lenient.Int(p, "hours") is > 0 and var h ? h : (int?)null;
        var requests = Lenient.Array(a, "requests").Select(Request).OfType<WorkRequest>().ToList();
        var skipped = Lenient.Array(a, "skipped").Where(k => k.ValueKind == JsonValueKind.Object && Lenient.At(k, "due") is not null)
            .Select(k => new WorkSkipped(Lenient.At(k, "due")!.Value, Lenient.Text(k, "why") ?? "", Lenient.Text(k, "words") ?? "")).ToList();
        return new(summary, Lenient.Field(a, "health") is { ValueKind: JsonValueKind.Object } health ? TafHealth.Read(health) : null,
            oldest, promise, requests, skipped);
    }

    static WorkRequest? Request(JsonElement r)
    {
        if (r.ValueKind != JsonValueKind.Object || Lenient.Text(r, "id") is not { } id || Lenient.At(r, "at") is not { } at) return null;
        var deliveries = Lenient.Array(r, "deliveries").Where(d => d.ValueKind == JsonValueKind.Object)
            .Select(d => new WorkDelivery(Lenient.Text(d, "place") ?? "door_reply", Lenient.Text(d, "key"), Lenient.Text(d, "state") ?? "",
                Lenient.Text(d, "line") ?? "", Lenient.Bool(d, "send_again"))).ToList();
        var credits = Lenient.Field(r, "credits") is { ValueKind: JsonValueKind.Object } c && Lenient.Text(c, "words")?.Trim() is { Length: > 0 } used ? used : null;
        return new(id, at, Lenient.Text(r, "state") ?? "", Lenient.Text(r, "line") ?? "", deliveries, credits);
    }
}

using System.Globalization;
using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>One trial of a version that ended: when it started, why it ended, and its calls, held and refused.</summary>
public sealed record TrialSummary(DateTimeOffset Started, string Why, int Calls, int Held, int Refused);

/// <summary>
/// One version of a taf, like an app build: its number, what changed, who made it in tafs cloud's words,
/// when, whether it's live, whether it brings a new Card (a KIFF draft), and its trials that ended.
/// </summary>
public sealed record TafVersion(int N, string? Note, string? ByWords, DateTimeOffset Created, bool Live, string? Draft, IReadOnlyList<TrialSummary> Trials);

/// <summary>
/// A taf's versions (<c>GET /v1/tafs/{id}/versions</c>), as the iPad's <c>VersionsPanel</c> lists them: newest first, the live
/// one marked, each with its line and its ended trials. A switch under way is said (<see cref="Switching"/>). Installing and
/// trying a version sign a Card switch, and come with their own change.
/// </summary>
public sealed record TafVersions(IReadOnlyList<TafVersion> Versions, int? Switching)
{
    public static string Path(string taf) => "/v1/tafs/" + CloudRead.PathPart(taf) + "/versions";

    /// <summary>Newest first, as the panel draws them.</summary>
    public IReadOnlyList<TafVersion> Newest => Versions.OrderByDescending(v => v.N).ToList();

    /// <summary>tafs cloud's answer; a version it can't read is left out. None for one that isn't an object with a list.</summary>
    public static TafVersions? Read(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || Lenient.Field(a, "versions") is not { ValueKind: JsonValueKind.Array } list) return null;
        var versions = list.EnumerateArray().Select(Version).OfType<TafVersion>().ToList();
        var switching = Lenient.Field(a, "switching") is { ValueKind: JsonValueKind.Object } s ? Lenient.Int(s, "n") : null;
        return new(versions, switching);
    }

    static TafVersion? Version(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object || Lenient.Int(v, "n") is not { } n || Lenient.At(v, "created") is not { } created) return null;
        var trials = Lenient.Array(v, "trials").Where(t => t.ValueKind == JsonValueKind.Object && Lenient.At(t, "started") is not null)
            .Select(t => new TrialSummary(Lenient.At(t, "started")!.Value, Lenient.Text(t, "why") ?? "", Lenient.Int(t, "calls") ?? 0,
                Lenient.Int(t, "held") ?? 0, Lenient.Int(t, "refused") ?? 0)).ToList();
        return new(n, Lenient.Text(v, "note"), Lenient.Text(v, "by_words"), created, Lenient.Bool(v, "live"), Lenient.Text(v, "draft"), trials);
    }

    /// <summary>"By you · 15 Nov 2026, 14:30 · brings a new Card": who, when (in the person's zone and language), and a new Card.</summary>
    public static string Byline(WordBook w, TafVersion v, TimeZoneInfo zone) =>
        string.Join(" · ", new[]
        {
            v.ByWords,
            TimeZoneInfo.ConvertTime(v.Created, zone).ToString("d MMM yyyy, HH:mm", Tafs.Core.Words.Locale.Culture(w.Language)),
            v.Draft is null ? null : w.Get("version.brings_new_card"),
        }.OfType<string>());

    /// <summary>One ended trial, in a line: "Tried 21 Oct · 3 calls, 1 held · went back".</summary>
    public static string Tried(WordBook w, TrialSummary t, TimeZoneInfo zone)
    {
        var counts = string.Join(", ", new[]
        {
            w.Count("version.tried.calls", t.Calls),
            t.Held > 0 ? w.Count("version.tried.held", t.Held) : null,
            t.Refused > 0 ? w.Count("version.tried.refused", t.Refused) : null,
        }.OfType<string>());
        var how = t.Why switch
        {
            "installed" => w.Get("version.tried.installed"),
            "back" => w.Get("version.tried.went_back"),
            "idle" => w.Get("version.tried.ended_unused"),
            _ => w.Get("version.tried.ended"),
        };
        var on = w.Get("version.tried.on", ("date", TimeZoneInfo.ConvertTime(t.Started, zone).ToString("d MMM", Tafs.Core.Words.Locale.Culture(w.Language))));
        return string.Join(" · ", on, counts, how);
    }
}

/// <summary>One run in a taf's credits: when it ended, tafs cloud's words for it, and whether it opens its request.</summary>
public sealed record CreditRun(string Run, DateTimeOffset At, string Words, bool Opens);

/// <summary>
/// A taf's credits this period (<c>GET /v1/usage/credits/tafs/{id}</c>), as the iPad's <c>TafCreditsView</c>: tafs cloud's
/// line for the period, then its runs newest first, 50 a page, More while there's a next.
/// </summary>
public sealed record TafCredits(string Words, IReadOnlyList<CreditRun> Runs, string? Next)
{
    /// <param name="previous">The period before (<c>period=previous</c>), as Plan &amp; usage's Last period opens it.</param>
    public static string Path(string taf, string? after = null, bool previous = false)
    {
        var query = new[] { previous ? "period=previous" : null, after is null ? null : "after=" + CloudRead.QueryPart(after) }.OfType<string>().ToList();
        return "/v1/usage/credits/tafs/" + CloudRead.PathPart(taf) + (query.Count == 0 ? "" : "?" + string.Join('&', query));
    }

    public static TafCredits? Read(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) return null;
        var runs = Lenient.Array(a, "items").Where(i => i.ValueKind == JsonValueKind.Object && Lenient.Text(i, "run") is not null && Lenient.At(i, "at") is not null)
            .Select(i => new CreditRun(Lenient.Text(i, "run")!, Lenient.At(i, "at")!.Value, Lenient.Text(i, "words") ?? "", Lenient.Bool(i, "opens"))).ToList();
        return new(Lenient.Text(a, "words") ?? "", runs, Lenient.Text(a, "next"));
    }

    /// <summary>A next page added to this one.</summary>
    public TafCredits With(TafCredits page) => new(Words, [.. Runs, .. page.Runs.Where(r => Runs.All(o => o.Run != r.Run))], page.Next);
}

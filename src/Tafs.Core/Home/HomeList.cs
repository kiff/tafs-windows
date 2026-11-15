using System.Globalization;
using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Home;

/// <summary>
/// One taf as Home lists it: its state (tafs cloud's word), what it's doing now in the app's words, the line that says what
/// went wrong when it needs looking at (<paramref name="Attention"/>), its Cards, how
/// many of its calls wait, and when it last changed.
/// </summary>
public sealed record HomeTaf(string Id, string Name, int Colour, string State, string Now, string? Attention,
    IReadOnlyList<TafCard> Cards, int Waiting, DateTimeOffset? LastRun, DateTimeOffset? LastChange)
{
    /// <summary>The state its page's head says: tafs cloud's word, Ready for an idle taf, Stopped for one it doesn't know.</summary>
    public string StateKey => State switch
    {
        "working" or "waiting" or "paused" or "halted" => State,
        "idle" => "ready",
        _ => "stopped",
    };
}

/// <summary>Home's answer: the tafs, and its headline ("2 tafs working."), counted by tafs cloud, never here.</summary>
public sealed record HomeAnswer(IReadOnlyList<HomeTaf> Tafs, string? Headline);

/// <summary>
/// The app's list of tafs: <c>GET /v1/home</c> on tafs cloud (contracts/home.json), as the iPad's KIFFMapping.taf. It
/// fills the Overview, Your tafs and a taf's head. What can't be read reads as none, never the whole answer.
/// </summary>
public static class HomeList
{
    public static readonly Uri Api = Kiff.KeptSessions.TafsCloud;
    public const string Path = "/v1/home";

    /// <summary>The tafs in tafs cloud's order; one without an id or a name is left out.</summary>
    public static HomeAnswer Read(WordBook w, JsonElement answer, TimeZoneInfo zone) => new(
        Lenient.Array(answer, "tafs").Where(t => Lenient.Text(t, "id") is not null && Lenient.Text(t, "name") is not null)
            .Select(t => Taf(w, t, zone)).ToList(),
        Lenient.Field(answer, "headline") is { } h && Lenient.Text(h, "words") is { Length: > 0 } words ? words : null);

    static HomeTaf Taf(WordBook w, JsonElement t, TimeZoneInfo zone)
    {
        var state = Lenient.Text(t, "state") ?? "";
        var health = Lenient.Field(t, "health") ?? default;
        return new(Lenient.Text(t, "id")!, Lenient.Text(t, "name")!, Lenient.Int(t, "color") ?? 0, state, Now(w, t, state, zone),
            Attention(t, Lenient.Text(health, "level") is not null and not "healthy" && Lenient.Array(health, "lines").Any()),
            Lenient.Field(t, "card") is { } card ? TafCards.Read(w, card) : [],
            Lenient.Int(t, "waiting") ?? 0, Lenient.At(t, "last_run"), Lenient.At(t, "last_change"));
    }

    /// <summary>What it's doing, as the iPad says it: tafs cloud's "Next run Fri 12:30" while it works on a schedule.</summary>
    static string Now(WordBook w, JsonElement t, string state, TimeZoneInfo zone) => state switch
    {
        "waiting" => w.Get("taf.now.waiting_on_you"),
        "paused" => w.Get("taf.state.paused"),
        "halted" => w.Get("taf.now.halted"),
        "working" when Lenient.Bool(t, "unscheduled") => w.Get("taf.now.works_from_chat"),
        "working" => Lenient.Text(t, "next_run_words")
            ?? (Lenient.At(t, "next_run") is { } next
                ? w.Get("taf.now.next_run", ("time", TimeZoneInfo.ConvertTime(next, zone).ToString("HH:mm", CultureInfo.InvariantCulture)))
                : w.Get("taf.now.running")),
        "idle" => w.Get("taf.now.acting_under_kiff"),
        _ => w.Get("taf.now.card_not_active"),
    };

    /// <summary>
    /// tafs cloud's work line, when it flags it (<c>work.attention</c>) or an idle taf isn't healthy: what went wrong last,
    /// said where its next run would be.
    /// </summary>
    static string? Attention(JsonElement t, bool unhealthy)
    {
        var work = Lenient.Field(t, "work") ?? default;
        var line = Lenient.Text(work, "line")?.Trim();
        var flagged = Lenient.Bool(work, "attention") || (Lenient.Text(work, "status") == "idle" && unhealthy);
        return flagged && !string.IsNullOrEmpty(line) ? line : null;
    }

    /// <summary>Its Card on the Overview: the first Card's figure and words ("€800 left today of €1,000"), else "–".</summary>
    public static string CardLine(WordBook w, HomeTaf t) =>
        t.Cards.FirstOrDefault()?.Hero(w) is { } hero ? $"{hero.Figure} {hero.Words}" : "–";

    /// <summary>
    /// The Overview's lines, every taf, with each one's Last result from <paramref name="reports"/> (its latest run, by its
    /// id) once read; until then when it last ran, else "Nothing yet", as the iPad's.
    /// </summary>
    public static IReadOnlyList<OverviewRow> Rows(WordBook w, HomeAnswer home, TimeZoneInfo zone, IReadOnlyDictionary<string, LatestRun>? reports = null) =>
        home.Tafs.Select(t => new OverviewRow(t.Id, t.Name, t.Colour, t.Now, CardLine(w, t),
            TafRuns.LastResult(w, reports?.GetValueOrDefault(t.Id), t.LastRun, zone))).ToList();

    /// <summary>
    /// Your tafs: those relevant now (a call of its waits, or it ran or changed in the last 24 hours), each with what went
    /// wrong in place of what it's doing when there's a line for it, as the iPad's sidebar.
    /// </summary>
    public static IReadOnlyList<OverviewRow> YourTafs(WordBook w, HomeAnswer home, DateTimeOffset now) =>
        home.Tafs.Where(t => Sidebar.RelevantNow(t.Waiting > 0 || t.State == "waiting", false, false, t.LastRun, t.LastChange, now))
            .Select(t => new OverviewRow(t.Id, t.Name, t.Colour, t.Attention ?? t.Now, CardLine(w, t), "–")).ToList();
}

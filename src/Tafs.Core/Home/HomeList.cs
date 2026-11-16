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
/// <summary>
/// When it starts and where its results go, as Home sends them: its next run (in tafs cloud's words when it sends them),
/// whether it works from its chat alone, and tafs' own places it delivers to (none when Home doesn't say).
/// </summary>
public sealed record TafStarts(DateTimeOffset? NextRun, string? NextRunWords, bool Unscheduled, IReadOnlyList<string>? Results);

public sealed record HomeTaf(string Id, string Name, int Colour, string State, string Now, string? Attention,
    IReadOnlyList<TafCard> Cards, int Waiting, DateTimeOffset? LastRun, DateTimeOffset? LastChange, string Job = "", string? Agent = null,
    bool Revoked = false, IReadOnlyList<string>? LabelIds = null, IReadOnlyList<string>? CardIdList = null, TafStarts? Starts = null)
{
    /// <summary>Its Cards' ids in KIFF (<c>card.ids</c>), revoked before it's removed for good; none without.</summary>
    public IReadOnlyList<string> CardIds => CardIdList ?? [];

    /// <summary>Its labels' ids, as Home sends them (<c>labels: [id]</c>); none without.</summary>
    public IReadOnlyList<string> Labels => LabelIds ?? [];

    /// <summary>The state its page's head says: tafs cloud's word, Ready for an idle taf, Stopped for one it doesn't know.</summary>
    public string StateKey => State switch
    {
        "working" or "waiting" or "paused" or "halted" => State,
        "idle" => "ready",
        _ => "stopped",
    };
}

/// <summary>
/// Home's answer: the tafs, its headline ("2 tafs working."), counted by tafs cloud, never here, the account's labels
/// (none when no taf is labelled), and the tafs whose setup stopped partway.
/// </summary>
public sealed record HomeAnswer(IReadOnlyList<HomeTaf> Tafs, string? Headline, HomeLabels? LabelList = null,
    IReadOnlyList<SettingUp>? SettingUpList = null)
{
    public HomeLabels Labels => LabelList ?? HomeLabels.None;

    /// <summary>The tafs being set up, listed among the tafs; none from a tafs cloud from before them.</summary>
    public IReadOnlyList<SettingUp> SettingUp => SettingUpList ?? [];
}

/// <summary>"Finish setting up", as the iPad's <c>HomeTaf.Finish</c>: the Coach chat where it stopped, and tafs cloud's words for it.</summary>
public sealed record SetupFinish(string Taf, string Session, string Words);

/// <summary>Discard's words, tafs cloud's, as the iPad's <c>SettingUp.Discard</c>: the button, and its confirmation.</summary>
public sealed record DiscardWords(string Button, string Title, string Text, string Yes, string No);

/// <summary>
/// A taf whose setup stopped partway (<c>setting_up</c>, kiff/tafs <c>docs/tafs.md</c>), as the iPad's <c>SettingUp</c>: the
/// name Coach gave it, its agent and colour; for the person whose Coach chat it was, the chat that finishes it, and Discard
/// with the Cards to revoke first.
/// </summary>
public sealed record SettingUp(string Name, string? Agent, int Colour, SetupFinish? Finish, DiscardWords? Discard, IReadOnlyList<string> Cards)
{
    /// <summary>Its row's line: where its setup stopped, in tafs cloud's words; being set up in someone else's chat, none.</summary>
    public string? Line => Finish?.Words;

    /// <summary>
    /// Whether its row finishes it, as the iPad's <c>canFinishSetup</c>: only Coach's own chat, for someone who may use Coach.
    /// Otherwise the row opens nothing.
    /// </summary>
    public bool Finishes(string? coach, bool mayUseCoach) => mayUseCoach && coach is not null && Finish?.Taf == coach;
}

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
        Lenient.Field(answer, "headline") is { } h && Lenient.Text(h, "words") is { Length: > 0 } words ? words : null,
        LabelChips.Read(answer),
        Lenient.Field(answer, "setting_up") is { ValueKind: JsonValueKind.Array } half ? SettingUps(half) : null);

    /// <summary>
    /// The tafs being set up, as the iPad reads them: one with no name is left out; a finish or Discard it can't draw (no
    /// chat or words; no button or yes) is none. Its Cards are plain ids or <c>{id}</c> each.
    /// </summary>
    static List<SettingUp> SettingUps(JsonElement half) =>
        half.EnumerateArray().Select(s =>
        {
            string T(JsonElement o, string n) => Lenient.Text(o, n)?.Trim() ?? "";
            var f = Lenient.Field(s, "finish") ?? default;
            var finish = new SetupFinish(T(f, "taf"), T(f, "session"), T(f, "words"));
            var discard = Lenient.Field(s, "discard") ?? default;
            var d = Lenient.Field(discard, "words") ?? default;
            var words = new DiscardWords(T(d, "button"), T(d, "title"), T(d, "text"), T(d, "yes"), T(d, "no"));
            var cards = Lenient.Array(discard, "cards").Select(c => c.ValueKind == JsonValueKind.String ? c.GetString() : Lenient.Text(c, "id"))
                .OfType<string>().ToList();
            return new SettingUp(T(s, "name"), Lenient.Text(s, "kiff_agent") is { Length: > 0 } agent ? agent : null, Lenient.Int(s, "color") ?? 0,
                finish is { Taf.Length: > 0, Session.Length: > 0, Words.Length: > 0 } ? finish : null,
                words is { Button.Length: > 0, Yes.Length: > 0 } ? words : null, cards);
        }).Where(s => s.Name.Length > 0).ToList();

    static HomeTaf Taf(WordBook w, JsonElement t, TimeZoneInfo zone)
    {
        var state = Lenient.Text(t, "state") ?? "";
        var health = Lenient.Field(t, "health") ?? default;
        return new(Lenient.Text(t, "id")!, Lenient.Text(t, "name")!, Lenient.Int(t, "color") ?? 0, state, Now(w, t, state, zone),
            Attention(t, Lenient.Text(health, "level") is not null and not "healthy" && Lenient.Array(health, "lines").Any()),
            Lenient.Field(t, "card") is { } card ? TafCards.Shown(w, card, Lenient.Text(t, "name")!) : [],
            Lenient.Int(t, "waiting") ?? 0, Lenient.At(t, "last_run"), Lenient.At(t, "last_change"),
            // Its job as Coach said it, else as the iPad's: an agent tafs doesn't run is described by its Cards.
            Lenient.Text(t, "job_summary") ?? w.Count("taf.works_under_cards", (Lenient.Field(t, "card") is { } c ? Lenient.Array(c, "ids").Count() : 0)), Lenient.Text(t, "kiff_agent"),
            // Its Card revoked, as the iPad's card.revoked (!card.active): every Card on its page reads Revoked and fades.
            Revoked: Lenient.Field(t, "card") is { } held && Lenient.Field(held, "active") is { ValueKind: JsonValueKind.False },
            LabelIds: LabelChips.Ids(t),
            CardIdList: Lenient.Field(t, "card") is { } ids ? Lenient.Array(ids, "ids").Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!).ToList() : null,
            Starts: new TafStarts(Lenient.At(t, "next_run"), Lenient.Text(t, "next_run_words"), Lenient.Bool(t, "unscheduled"),
                Lenient.Field(t, "results") is { ValueKind: JsonValueKind.Array } r ? r.EnumerateArray().Select(Tafs.Core.Taf.ResultPlaces.Of).OfType<string>().ToList() : null));
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
            TafRuns.LastResult(w, reports?.GetValueOrDefault(t.Id), t.LastRun, zone), t.Labels,
            LabelChips.TafChips(LabelChips.Of(t.Labels, home.Labels)))).ToList();

    /// <summary>
    /// Your tafs: those relevant now (a call of its waits, or it ran or changed in the last 24 hours), each with what went
    /// wrong in place of what it's doing when there's a line for it, as the iPad's sidebar.
    /// </summary>
    public static IReadOnlyList<OverviewRow> YourTafs(WordBook w, HomeAnswer home, DateTimeOffset now) =>
        home.Tafs.Where(t => Sidebar.RelevantNow(t.Waiting > 0 || t.State == "waiting", false, false, t.LastRun, t.LastChange, now))
            .Select(t => new OverviewRow(t.Id, t.Name, t.Colour, t.Attention ?? t.Now, CardLine(w, t), "–")).ToList();
}

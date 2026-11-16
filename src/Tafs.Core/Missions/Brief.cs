using System.Text.Json;
using Tafs.Core.Home;
using Tafs.Core.Words;

namespace Tafs.Core.Missions;

/// <summary>
/// The brief's headings, in the person's language: the tab's name ("Your brief"), when it was made and when the next one
/// comes, then Decisions, Done, Next move, Coach recommends, Meanwhile, Evidence, the first label chip and Urgent.
/// </summary>
public sealed record BriefWords(string Tab, string Title, string When, string Decisions, string Done, string NextMove,
    string Recommends, string Meanwhile, string Evidence, string All, string Urgent)
{
    public static readonly BriefWords None = new("", "", "", "", "", "", "", "", "", "", "");
}

/// <summary>What a decision is about: a held call (<c>hold</c>), a step (<c>step</c>), a connection, or a proposal.</summary>
public sealed record BriefRef(string Kind, string Id, string Taf)
{
    public static readonly BriefRef None = new("", "", "");
    /// <summary>The ref as Needs you's items are keyed: <c>hold:&lt;id&gt;</c>, <c>step:&lt;handoff&gt;</c>.</summary>
    public string Key => $"{Kind}:{Id}";
}

/// <summary>One answer: its words, what it answers, whether it's Coach's pick, and whether it's a decline.</summary>
public sealed record BriefAnswer(string Words, string Answer, bool Recommended, bool Decline);

/// <summary>
/// One decision: the question, its answers, why Coach recommends one, what happens meanwhile, its deadline in words,
/// whether it's urgent, Later's words when it can be put off, why it can't wait when it can't, a note, and what it's about.
/// </summary>
public sealed record BriefDecision(string Id, string Question, IReadOnlyList<BriefAnswer> Answers, string? Why, string Meanwhile,
    string? Deadline, bool Urgent, string? Later, string? CantWait, string? Note, BriefRef Ref)
{
    /// <summary>Coach's pick, drawn first and filled, only with its why; without one the answers are equals.</summary>
    public BriefAnswer? Recommended => Why is null ? null : Answers.FirstOrDefault(a => a.Recommended);

    /// <summary>Later is offered only when tafs cloud sends its words, and never for an urgent decision.</summary>
    public bool OffersLater => Later is not null && !Urgent;
}

/// <summary>One item a job works on (an invoice, a ticket): what was done, its state, and when it's next checked.</summary>
public sealed record BriefCase(string Name, string Words, string State, string? NextCheck)
{
    public bool Closed => State == "closed";
    public bool Waiting => State == "waiting";
}

/// <summary>What got done for a mission: its cases, what it's worth in words, and the mission whose log holds the evidence.</summary>
public sealed record BriefDone(string? Mission, IReadOnlyList<BriefCase> Cases, string? Value, string? Log);

/// <summary>One next move, with its Start and Dismiss words and the cases it's about.</summary>
public sealed record BriefNextMove(string Words, string Start, string Dismiss, IReadOnlyList<string> Cases);

/// <summary>A count and its words: decisions beyond the five shown (<c>more</c>), or the rest that need nothing now.</summary>
public sealed record BriefCounted(int Count, string Words);

/// <summary>
/// "Your brief" (Missions design v2.1 §7), from <c>GET /v1/brief?label=</c>, as the iPad's <c>TafsCloud.Brief</c>. Every
/// word, count, order and urgency is tafs cloud's: the app draws it and decides nothing.
/// </summary>
public sealed record Brief(string? At, string? NextAt, IReadOnlyList<BriefDecision> Decisions, BriefCounted? More,
    IReadOnlyList<BriefDone> Done, BriefNextMove? NextMove, BriefCounted? Rest, string? Credits, HomeLabels Labels, BriefWords Words)
{
    public const string Route = "/v1/brief";

    /// <summary>The brief, narrowed to a label's tafs; each label's count stays over the whole account.</summary>
    public static string Path(string? label) => label is null ? Route : $"{Route}?label={Uri.EscapeDataString(label)}";

    /// <summary>
    /// Later's route for a decision. Its id is <c>hold:&lt;id&gt;</c> or <c>step:&lt;handoff&gt;</c>, sent as one path
    /// segment: <c>:</c> is kept, as tafs cloud matches the id as written, and anything a segment can't hold, <c>/</c>
    /// included, is escaped, as the iPad's <c>briefLater</c>.
    /// </summary>
    public static string LaterPath(string decision) =>
        $"{Route}/decisions/{Uri.EscapeDataString(decision).Replace("%3A", ":", StringComparison.Ordinal)}/later";

    /// <summary>
    /// The brief, read leniently: a part the app can't read is left out, never the brief with it. A decision needs an id,
    /// a question and an answer with words; Done needs a case; the next move needs its three lines. Null for an answer
    /// that isn't an object.
    /// </summary>
    public static Brief? Read(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object) return null;
        return new(T(answer, "at"), T(answer, "next_at"),
            Items(answer, "decisions").Select(Decision).Where(d => d.Id.Length > 0 && d.Question.Length > 0 && d.Answers.Count > 0).ToList(),
            Obj(answer, "more") is { } m && Count(m) is { Count: > 0, Words.Length: > 0 } more ? more : null,
            Items(answer, "done").Select(DoneOf).Where(d => d.Cases.Count > 0).ToList(),
            Obj(answer, "next_move") is { } n && Move(n) is { Words.Length: > 0, Start.Length: > 0, Dismiss.Length: > 0 } move ? move : null,
            Obj(answer, "rest") is { } r && Count(r) is { Words.Length: > 0 } rest ? rest : null,
            Obj(answer, "credits") is { } c ? T(c, "words") : null,
            LabelChips.Read(answer),
            Obj(answer, "words") is { } w ? WordsOf(w) : BriefWords.None);
    }

    /// <summary>
    /// No brief to show: no decision, nothing done and no next move, so Needs you stays as today. The brief never hides
    /// what waits.
    /// </summary>
    public bool Empty => Decisions.Count == 0 && Done.Count == 0 && NextMove is null;

    /// <summary>
    /// Where a decision is answered: the route it already has, where its own signed answer is given. A held call opens on
    /// its own card in Needs you, with <b>no answer chosen</b>; a step on its own; anything else Needs you. The answer
    /// tapped is never part of where it goes, so nothing is chosen ahead and nothing is signed from the brief.
    /// </summary>
    public static BriefDestination Destination(BriefDecision decision) =>
        decision.Ref.Id.Length == 0 ? new(BriefGoes.NeedsYou, null)
        : decision.Ref.Kind switch
        {
            "hold" => new(BriefGoes.Hold, decision.Ref.Id),
            "step" => new(BriefGoes.Step, decision.Ref.Id),
            _ => new(BriefGoes.NeedsYou, null),
        };

    /// <summary>
    /// How much of Needs you the brief doesn't show, matched item by item, never by number, as the iPad's
    /// <c>needsYouBeyondBrief</c>: a held call or step the brief shows is matched by its ref (<c>hold:&lt;id&gt;</c>,
    /// <c>step:&lt;handoff&gt;</c>), and everything the brief can't carry (<paramref name="others"/>) counts.
    /// </summary>
    public static int NeedsYouBeyond(IEnumerable<string> items, int others, IEnumerable<BriefDecision> shown)
    {
        var keys = shown.Select(d => d.Ref.Key).ToHashSet();
        return items.Count(i => !keys.Contains(i)) + others;
    }

    static BriefDecision Decision(JsonElement d) => new(
        T(d, "id") ?? "", T(d, "question") ?? "",
        Items(d, "answers").Select(a => new BriefAnswer(T(a, "words") ?? "", T(a, "answer") ?? "", B(a, "recommended"), B(a, "decline")))
            .Where(a => a.Words.Length > 0).ToList(),
        T(d, "why"), T(d, "meanwhile") ?? "", T(d, "deadline"), B(d, "urgent"),
        Obj(d, "later") is { } l ? T(l, "words") : null,
        // tafs cloud's route says why it can't wait as now_why.
        T(d, "cant_wait") ?? T(d, "now_why"), T(d, "note"),
        Obj(d, "ref") is { } r ? new BriefRef(T(r, "kind") ?? "", T(r, "id") ?? "", T(r, "taf") ?? "") : BriefRef.None);

    static BriefDone DoneOf(JsonElement d) => new(
        // The mission as words, or as {id, name}.
        T(d, "mission") ?? (Obj(d, "mission") is { } m ? T(m, "name") : null),
        Items(d, "cases").Select(c => new BriefCase(T(c, "name") ?? "", T(c, "words") ?? "", T(c, "state") ?? "", T(c, "next_check")))
            .Where(c => c.Words.Length > 0 || c.Name.Length > 0).ToList(),
        T(d, "value"), T(d, "log") ?? (Obj(d, "mission") is { } mm ? T(mm, "id") : null));

    static BriefNextMove Move(JsonElement n) => new(T(n, "words") ?? "", T(n, "start") ?? "", T(n, "dismiss") ?? "",
        Items(n, "cases").Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).ToList());

    static BriefCounted Count(JsonElement c) =>
        new(c.TryGetProperty("count", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var i) ? i : 0, T(c, "words") ?? "");

    static BriefWords WordsOf(JsonElement w)
    {
        var title = T(w, "title") ?? "";
        // tafs cloud's route sends the tab's name as title ("Your brief", the glossary's words).
        return new(T(w, "tab") ?? title, title, T(w, "when") ?? "", T(w, "decisions") ?? "", T(w, "done") ?? "",
            T(w, "next_move") ?? "", T(w, "recommends") ?? "", T(w, "meanwhile") ?? "", T(w, "evidence") ?? "",
            T(w, "all") ?? "", T(w, "urgent") ?? "");
    }

    /// <summary>A line of text, trimmed; none when absent, blank or of another shape.</summary>
    internal static string? T(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        && v.GetString()!.Trim() is { Length: > 0 } t ? t : null;

    internal static bool B(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    internal static JsonElement? Obj(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    /// <summary>A list's items, each read on its own: one that isn't an object is left out, never the list.</summary>
    internal static IEnumerable<JsonElement> Items(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(i => i.ValueKind is JsonValueKind.Object or JsonValueKind.String) : [];
}

/// <summary>Where answering a decision goes.</summary>
public enum BriefGoes { NeedsYou, Hold, Step }

/// <summary>Where answering a decision goes, and the hold or hand-off it opens on.</summary>
public sealed record BriefDestination(BriefGoes Goes, string? Id);

/// <summary>One piece of a log entry's evidence: its kind, id and words.</summary>
public sealed record LogEvidence(string Kind, string Id, string Words);

/// <summary>
/// One entry of a mission's log: when, who wrote it (a taf, the lead Coach, or the person) and in words, its kind and in
/// words, one or two sentences, its evidence and value, and whether its taf was removed (greyed).
/// </summary>
public sealed record LogItem(string Id, string At, string By, string ByWords, string Kind, string KindWords, string Words,
    IReadOnlyList<LogEvidence> Evidence, string? Value, bool Removed);

/// <summary>
/// A mission's log (design v2.1 §6), from <c>GET /v1/missions/{id}/log</c>, as the iPad's <c>TafsCloud.MissionLog</c>: read
/// only, newest first, 20 at a time, <c>before</c> the next page's cursor.
/// </summary>
public sealed record MissionLog(IReadOnlyList<LogItem> Items, string? Before)
{
    public static string Path(string mission, string? before = null) =>
        $"/v1/missions/{Uri.EscapeDataString(mission)}/log" + (before is null ? "" : $"?before={Uri.EscapeDataString(before)}");

    /// <summary>
    /// The log, read leniently: an entry without words is left out. <c>by</c> is tafs cloud's <c>{kind: taf | lead |
    /// person, name?}</c>; with no name the shared words say who: "A taf", "Coach" or "You".
    /// </summary>
    public static MissionLog? Read(WordBook w, JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object) return null;
        var items = Brief.Items(answer, "items").Select(i =>
        {
            var by = Brief.T(i, "by") ?? "";
            var byWords = Brief.T(i, "by_words") ?? "";
            if (by.Length == 0 && Brief.Obj(i, "by") is { } who)
            {
                by = Brief.T(who, "kind") ?? "";
                if (byWords.Length == 0)
                    byWords = Brief.T(who, "name") ?? w.Get(by switch { "lead" => "coach.title", "person" => "common.you", _ => "needs_you.a_taf" });
            }
            var at = Brief.T(i, "at") ?? "";
            var words = Brief.T(i, "words") ?? "";
            return new LogItem(Brief.T(i, "id") ?? $"{at}|{by}|{words}", at, by, byWords, Brief.T(i, "kind") ?? "",
                Brief.T(i, "kind_words") ?? "", words,
                Brief.Items(i, "evidence").Select(e => new LogEvidence(Brief.T(e, "kind") ?? "", Brief.T(e, "id") ?? "", Brief.T(e, "words") ?? ""))
                    .Where(e => e.Words.Length > 0).ToList(),
                Brief.T(i, "value"), Brief.B(i, "removed"));
        }).Where(i => i.Words.Length > 0).ToList();
        return new(items, Brief.T(answer, "before"));
    }
}

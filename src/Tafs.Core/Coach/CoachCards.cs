using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>One choice of a question, as Coach wrote it: what's sent back, what it says, and what it means.</summary>
public sealed record CardChoice(string Value, string Label, string? Description = null);

/// <summary>One of several questions Coach asks at once, as the iPad's <c>SetupComponent.Question</c>: every word is Coach's.</summary>
public sealed record CardQuestion(string Id, string Text, IReadOnlyList<CardChoice> Options, bool Multi = false, string? Note = null);

/// <summary>
/// One card Coach shows in its thread, as the iPad's <c>SetupComponent</c>: its kind (<c>notice</c>, <c>confirm</c>,
/// <c>questions</c>…), its id (what an answer is sent for), its words, a confirm's options, a questions card's questions,
/// and whether its run waits on it. A card grants nothing.
/// </summary>
public sealed record CoachCard(string Kind, string Id, string? Title = null, string? Text = null, string? Tone = null,
    IReadOnlyList<CardChoice>? Options = null, IReadOnlyList<CardQuestion>? Questions = null, string? Waits = null,
    IReadOnlyList<PlanStep>? Steps = null, CardTaf? Taf = null, IReadOnlyList<string>? Tools = null, string? Yes = null)
{
    public static readonly string[] Kinds = ["steps", "notice", "confirm", "questions", "approval", "sign_in", "result", "knob", "drive", "door"];

    /// <summary>An approval's planque grant id, KIFF's draft id (the taf's Cards), and the vendor's host.</summary>
    public string? Grant { get; init; }
    public string? Draft { get; init; }
    public string? Service { get; init; }
    /// <summary>tafs cloud's line for a connection already connected (<c>connection: {done, words}</c>), drawn in place of its words.</summary>
    public string? ConnectionDone { get; init; }
    /// <summary>tafs cloud's mark of what the approval signs (<c>signs</c>): none when it didn't say.</summary>
    public CardSigns? Signs { get; init; }
    /// <summary>
    /// tafs cloud's words for a sign-in wider than the job (<c>wider: {words}</c>), in the reader's language:
    /// planque couldn't narrow it (<c>usual_access</c>), so it asks for more access than the taf uses. None when it didn't say.
    /// </summary>
    public string? Wider { get; init; }

    /// <summary>A door card's taf (tafs cloud's id) and its draft (Coach's <c>taf_door_api</c>), read from tafs cloud by these: the card carries no words of Coach's.</summary>
    public string? TafId { get; init; }
    public string? DraftId { get; init; }
}

/// <summary>
/// What an approval's signature covers, as tafs cloud marks it (the iPad's <c>SetupComponent.Signs</c>): whether the taf's
/// Card is in it, the Card section's heading when it isn't ("Its Card: you approve it next"), and whether Approve is a tap
/// in an approval window or the person every time. Read leniently: a field of another shape is none.
/// </summary>
public sealed record CardSigns(bool? Card, string? Words, string? Takes)
{
    /// <summary>The Card isn't signed by this approval: it comes next.</summary>
    public bool CardNext => Card == false;
}

/// <summary>
/// What sending a card's answer came to, as the iPad's <c>answer(_:_:)</c>: sent (the thread is read again); a card that
/// doesn't wait any more (409, read again); refused for want of the AI-data consent (the card stays as chosen); or not reached.
/// </summary>
public enum CardSent { Sent, ReadAgain, Consent, NotReached }

/// <summary>
/// The taf a result card hands over, as the iPad's <c>SetupComponent.Taf</c>: its KIFF agent and name; for an improvement,
/// the tafs cloud id of the taf it's the next version of (<paramref name="Improves"/>), and what changed.
/// </summary>
public sealed record CardTaf(string Agent, string Name, string? Improves = null, string? Note = null);

/// <summary>
/// What tafs cloud made of a result it answered itself, as the iPad's <c>CardAnswer.Outcome</c>, each part read on its own:
/// the taf it set up, the version it made, why it made neither, or its words when no plan lets the taf run.
/// </summary>
public sealed record CardOutcome(string? TafId, int? Version, string? Failed, string? PlanNeeded);

/// <summary>One step of Coach's plan, as the iPad's <c>SetupComponent.Step</c>: its words, and pending, active, done, failed or skipped.</summary>
public sealed record PlanStep(string Label, string Status)
{
    /// <summary>The step the plan is at, as the iPad's <c>SetupPlan.current</c>: the active one, else the first not yet done, else the last.</summary>
    public static PlanStep? Current(IReadOnlyList<PlanStep> steps) =>
        steps.FirstOrDefault(s => s.Status == "active") ?? steps.FirstOrDefault(s => s.Status is "pending" or "failed") ?? steps.LastOrDefault();
}

/// <summary>
/// A field's shape as Swift's decoder checks it: a required field is there and of its shape; an optional one is absent,
/// null, or of its shape. Words are strings; a yes-or-no is true or false; a number is whole.
/// </summary>
static class Shape
{
    public static bool Required(JsonElement o, string name, Func<JsonElement, bool> fits) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && fits(v);

    public static bool Optional(JsonElement o, string name, Func<JsonElement, bool> fits) =>
        o.ValueKind == JsonValueKind.Object && (!o.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null || fits(v));

    public static bool Text(JsonElement v) => v.ValueKind == JsonValueKind.String;

    public static bool Bool(JsonElement v) => v.ValueKind is JsonValueKind.True or JsonValueKind.False;

    public static bool Int(JsonElement v) => v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && d == Math.Floor(d) && Math.Abs(d) < 9e15;

    public static bool List(JsonElement v, Func<JsonElement, bool> each) => v.ValueKind == JsonValueKind.Array && v.EnumerateArray().All(each);
}

/// <summary>A card's answer as kiff/agents recorded it (the iPad's <c>CardAnswer</c>): its state, its words, and when.</summary>
/// <param name="Answer">tafs cloud's stored answer, free of any language (<c>approved</c>, <c>declined</c>, <c>failed: …</c>): what an
/// approval's outcome is read from. None from a tafs cloud before it.</param>
public sealed record CardAnswer(string State, string Words, DateTimeOffset? At, CardOutcome? Outcome = null, string? Answer = null);

/// <summary>
/// A card as the thread holds it (the iPad's <c>ShownComponent</c>): where it first appeared, its latest version, and its
/// answer once recorded. <see cref="Ref"/> is which card it is: tafs cloud's key, else its conversation and id.
/// </summary>
public sealed record ShownCard(DateTimeOffset FirstAt, string Session, string Run, CoachCard Card, CardAnswer? Answer = null, string? Key = null)
{
    public string Ref => Key ?? $"{Session}#{Card.Id}";

    /// <summary>
    /// A shown card from tafs cloud, as the iPad's <c>Shown</c>: one without its time, conversation, run or a card it can read
    /// is left out; an answer it can't read leaves the card unanswered.
    /// </summary>
    public static ShownCard? Read(JsonElement s)
    {
        if (Lenient.At(s, "at") is not { } at || Lenient.Text(s, "session") is not { } session || Lenient.Text(s, "run") is not { } run
            || Lenient.Field(s, "component") is not { } c || ReadCard(c) is not { } card) return null;
        return new(at, session, run, card, Recorded(Lenient.Field(s, "answer")), Lenient.Text(s, "key"));
    }

    /// <summary>
    /// A recorded answer, as the iPad's <c>CardAnswer</c> read with <c>try?</c>: its state, words and who answered are each
    /// absent, null or words, or the card is unanswered; a time it can't read is no time.
    /// </summary>
    static CardAnswer? Recorded(JsonElement? field)
    {
        if (field is not { ValueKind: JsonValueKind.Object } a || !Shape.Optional(a, "state", Shape.Text) || !Shape.Optional(a, "words", Shape.Text)
            || !Shape.Optional(a, "by", Shape.Text)) return null;
        return new CardAnswer(Lenient.Text(a, "state") ?? "answered", Lenient.Text(a, "words") ?? "", Lenient.At(a, "at"), Outcome(Lenient.Field(a, "outcome")),
            Lenient.Text(a, "answer"));
    }

    /// <summary>The outcome as the iPad reads it with <c>try?</c>, part by part: one part of another shape is none, never the rest.</summary>
    static CardOutcome? Outcome(JsonElement? field)
    {
        if (field is not { ValueKind: JsonValueKind.Object } o) return null;
        var planNeeded = Lenient.Field(o, "plan_needed") is { ValueKind: JsonValueKind.Object } p && Lenient.Text(p, "words")?.Trim() is { Length: > 0 } words
            ? words : null;
        return new CardOutcome(Lenient.Text(o, "taf_id"), Lenient.Field(o, "version") is { } v && Shape.Int(v) ? (int)v.GetDouble() : null,
            Lenient.Text(o, "failed"), planNeeded);
    }

    /// <summary>
    /// A card as the iPad's synthesized <c>SetupComponent</c> decoder reads it: its kind (one this app knows) and id are
    /// required; every other field it declares is absent, null, or of its own shape, and one of another shape fails the card,
    /// whether this app keeps it yet or not.
    /// </summary>
    static CoachCard? ReadCard(JsonElement c)
    {
        if (c.ValueKind != JsonValueKind.Object || !Shape.Required(c, "type", Shape.Text) || !CoachCard.Kinds.Contains(c.GetProperty("type").GetString())
            || !Shape.Required(c, "id", Shape.Text)) return null;
        foreach (var text in (string[])["title", "text", "tone", "grant", "draft", "service", "yes", "no", "note", "waits", "taf_id", "draft_id"])
            if (!Shape.Optional(c, text, Shape.Text)) return null;
        if (!Shape.Optional(c, "steps", v => Shape.List(v, Step)) || !Shape.Optional(c, "options", v => Shape.List(v, Option))
            || !Shape.Optional(c, "questions", v => Shape.List(v, Question)) || !Shape.Optional(c, "taf", Taf)
            || !Shape.Optional(c, "tools", v => Shape.List(v, Shape.Text)) || !Shape.Optional(c, "version", Shape.Int)
            || !Shape.Optional(c, "connection", Connection) || !Shape.Optional(c, "signs", v => v.ValueKind == JsonValueKind.Object)) return null;
        var questions = Lenient.Field(c, "questions") is { ValueKind: JsonValueKind.Array } qs
            ? qs.EnumerateArray().Select(q => new CardQuestion(Lenient.Text(q, "id")!, Lenient.Text(q, "text")!, Choices(q.GetProperty("options")),
                Lenient.Bool(q, "multi"), Lenient.Text(q, "note"))).ToList()
            : null;
        var options = Lenient.Field(c, "options") is { ValueKind: JsonValueKind.Array } os ? Choices(os) : null;
        var steps = Lenient.Field(c, "steps") is { ValueKind: JsonValueKind.Array } ss
            ? ss.EnumerateArray().Select(x => new PlanStep(Lenient.Text(x, "label")!, Lenient.Text(x, "status")!)).ToList()
            : null;
        return new CoachCard(c.GetProperty("type").GetString()!, c.GetProperty("id").GetString()!, Lenient.Text(c, "title"), Lenient.Text(c, "text"),
            Lenient.Text(c, "tone"), options, questions, Lenient.Text(c, "waits"), steps,
            Lenient.Field(c, "taf") is { ValueKind: JsonValueKind.Object } t
                ? new CardTaf(Lenient.Text(t, "agent")!, Lenient.Text(t, "name")!, Lenient.Text(t, "improves"), Lenient.Text(t, "note")) : null,
            Lenient.Field(c, "tools") is { ValueKind: JsonValueKind.Array } tools ? tools.EnumerateArray().Select(x => x.GetString()!).ToList() : null,
            Lenient.Text(c, "yes"))
        {
            Grant = Lenient.Text(c, "grant"), Draft = Lenient.Text(c, "draft"), Service = Lenient.Text(c, "service"),
            TafId = Lenient.Text(c, "taf_id"), DraftId = Lenient.Text(c, "draft_id"),
            ConnectionDone = Lenient.Field(c, "connection") is { } done && Lenient.Bool(done, "done") && Lenient.Text(done, "words") is { } line
                && line.Trim().Length > 0 ? line : null,
            Signs = Lenient.Field(c, "signs") is { ValueKind: JsonValueKind.Object } s
                ? new CardSigns(Lenient.Field(s, "card") is { ValueKind: JsonValueKind.True or JsonValueKind.False } card ? card.GetBoolean() : null,
                    Lenient.Text(s, "words") is { } words && words.Trim().Length > 0 ? words : null, Lenient.Text(s, "takes"))
                : null,
            // Read leniently, as signs: another shape is no line, never the card.
            Wider = Lenient.Field(c, "wider") is { ValueKind: JsonValueKind.Object } wider && Lenient.Text(wider, "words") is { } said
                && said.Trim().Length > 0 ? said.Trim() : null,
        };
    }

    // The shapes inside a card, as the iPad declares them: Step, Option, Question with its Choice, Taf, Connection.
    static bool Step(JsonElement v) => Shape.Required(v, "label", Shape.Text) && Shape.Required(v, "status", Shape.Text);

    static bool Option(JsonElement v) => Shape.Required(v, "value", Shape.Text) && Shape.Required(v, "label", Shape.Text);

    static bool Choice(JsonElement v) => Option(v) && Shape.Optional(v, "description", Shape.Text);

    static bool Question(JsonElement v) => Shape.Required(v, "id", Shape.Text) && Shape.Required(v, "text", Shape.Text)
        && Shape.Required(v, "options", o => Shape.List(o, Choice)) && Shape.Optional(v, "multi", Shape.Bool) && Shape.Optional(v, "note", Shape.Text);

    static bool Taf(JsonElement v) => Shape.Required(v, "agent", Shape.Text) && Shape.Required(v, "name", Shape.Text)
        && ((string[])["job", "draft", "improves", "note", "try", "notes"]).All(f => Shape.Optional(v, f, Shape.Text)) && Shape.Optional(v, "unscheduled", Shape.Bool);

    static bool Connection(JsonElement v) => v.ValueKind == JsonValueKind.Object && Shape.Optional(v, "done", Shape.Bool) && Shape.Optional(v, "words", Shape.Text);

    static List<CardChoice> Choices(JsonElement a) =>
        a.EnumerateArray().Select(o => new CardChoice(Lenient.Text(o, "value")!, Lenient.Text(o, "label")!, Lenient.Text(o, "description"))).ToList();

    /// <summary>
    /// The cards in the order they first appeared, as the iPad's <c>SetupThread.fold</c>: a card shown again replaces its
    /// earlier version in place, its answer with it (a card shown again waits anew).
    /// </summary>
    public static IReadOnlyList<ShownCard> Fold(IEnumerable<ShownCard> shown)
    {
        var folded = new List<ShownCard>();
        foreach (var s in shown)
        {
            var i = folded.FindIndex(f => f.Ref == s.Ref);
            if (i >= 0) folded[i] = folded[i] with { Card = s.Card, Answer = s.Answer };
            else folded.Add(s);
        }
        return folded;
    }
}

/// <summary>
/// An answer to a card, as the person's message carries it ("[questions q1] {…}", the iPad's <c>SetupThread.Answer</c>): the
/// card's kind and id, the answer, and kiff/agents' words for it when it came as data.
/// </summary>
public sealed record CardReply(string Kind, string Id, string Text, string? Words = null)
{
    public bool Failed => Text.StartsWith("failed", StringComparison.Ordinal);

    public static CardReply? Parse(string message)
    {
        if (!message.StartsWith('[') || message.IndexOf(']') is var close && close < 0) return null;
        var head = message[1..close].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (head.Length != 2 || !CoachCard.Kinds.Contains(head[0])) return null;
        return new CardReply(head[0], head[1], message[(close + 1)..].Trim(' ', '\t'));
    }

    /// <summary>
    /// A recorded answer in the form the cards read, as the iPad's <c>answer(_:to:)</c>: none unless answered; a confirm's
    /// option by its label; an approval's, sign-in's or drive's outcome; otherwise its words.
    /// <para>
    /// An approval's outcome comes from tafs cloud's stored <see cref="CardAnswer.Answer"/>, free of any language. Without it, it's
    /// approved only for exactly tafs cloud's English "Approved" or "Already approved…", and anything else is not approved: the words
    /// come in the person's language ("Refusé", "Rechazado"), and an answer that can't be read never reads as approved.
    /// </para>
    /// </summary>
    public static CardReply? Of(CardAnswer recorded, CoachCard card)
    {
        if (recorded.State != "answered") return null;
        var words = recorded.Words;
        var at = words.IndexOf(" · it didn't go through: ", StringComparison.Ordinal);
        var failed = at < 0 ? null : "failed: " + words[(at + " · it didn't go through: ".Length)..];
        var stored = Stored(recorded.Answer, card.Kind);
        var text = card.Kind switch
        {
            "confirm" => card.Options?.FirstOrDefault(o => o.Label == words)?.Value ?? words,
            "approval" => stored ?? failed ?? (words == "Approved" ? "approved"
                : words.StartsWith("Already approved", StringComparison.Ordinal) ? "already approved" : "declined"),
            "sign_in" => stored ?? failed ?? (words == "Signed in" ? "signed in" : "not now"),
            "drive" => stored ?? (words.ToLowerInvariant().StartsWith("approved", StringComparison.Ordinal) ? "approved" : "declined"),
            _ => words,
        };
        return new CardReply(card.Kind, card.Id, text, words);
    }

    /// <summary>tafs cloud's stored answer when it's one this kind of card reads; none otherwise, and the words decide, closed.</summary>
    static string? Stored(string? answer, string kind)
    {
        if (answer?.Trim() is not { Length: > 0 } a) return null;
        if (a.StartsWith("failed", StringComparison.Ordinal)) return a;
        // tafs cloud's settled answer: connected, with its Card next, is done.
        if (kind == "approval" && a == "connected") return "approved";
        string[] known = kind switch
        {
            "approval" => ["approved", "declined", "already approved"],
            "sign_in" => ["signed in", "not now", "declined"],
            "drive" => ["approved", "declined"],
            _ => [],
        };
        return known.Contains(a) || (kind == "approval" && a.StartsWith("already approved", StringComparison.Ordinal)) ? a : null;
    }
}

/// <summary>A questions card's answer to one question: one of Coach's choices, several, or the person's own words.</summary>
public abstract record QuestionAnswer
{
    public sealed record Choice(string Value) : QuestionAnswer;
    public sealed record Choices(IReadOnlyList<string> Values) : QuestionAnswer;
    public sealed record Own(string Text) : QuestionAnswer;
}

/// <summary>
/// A questions card's answers, as the iPad's <c>QuestionAnswers</c>: <c>{"&lt;question&gt;": {"value": …} | {"values": […]} |
/// {"own": "…"}}</c>, its keys sorted, so the same answers send the same words; and read back in the person's words.
/// </summary>
public static class QuestionAnswers
{
    static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Encode(IReadOnlyDictionary<string, QuestionAnswer> answers)
    {
        var o = new JsonObject();
        foreach (var (id, a) in answers.OrderBy(a => a.Key, StringComparer.Ordinal))
            o[id] = a switch
            {
                QuestionAnswer.Choice c => new JsonObject { ["value"] = c.Value },
                QuestionAnswer.Choices cs => new JsonObject { ["values"] = new JsonArray(cs.Values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()) },
                QuestionAnswer.Own own => new JsonObject { ["own"] = own.Text },
                _ => new JsonObject(),
            };
        return o.ToJsonString(Plain);
    }

    public static Dictionary<string, QuestionAnswer>? Decode(string text)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(text).RootElement; }
        catch (JsonException) { return null; }
        if (root.ValueKind != JsonValueKind.Object) return null;
        var answers = new Dictionary<string, QuestionAnswer>();
        foreach (var p in root.EnumerateObject())
        {
            if (p.Value.ValueKind != JsonValueKind.Object) return null;
            if (Lenient.Text(p.Value, "value") is { } v) answers[p.Name] = new QuestionAnswer.Choice(v);
            else if (Lenient.Field(p.Value, "values") is { ValueKind: JsonValueKind.Array } vs && vs.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String))
                answers[p.Name] = new QuestionAnswer.Choices(vs.EnumerateArray().Select(x => x.GetString()!).ToList());
            else if (Lenient.Text(p.Value, "own") is { } own) answers[p.Name] = new QuestionAnswer.Own(own);
        }
        return answers;
    }

    /// <summary>
    /// The answers in the person's words, in the card's order, then any others by id: "Test mode · Just one · In your words:
    /// “300 € on weekdays”". None when the text isn't answers.
    /// </summary>
    public static string? Words(WordBook w, string text, IReadOnlyList<CardQuestion> questions)
    {
        if (Decode(text) is not { } answers) return null;
        var order = questions.Select(q => q.Id).Concat(answers.Keys.Where(id => questions.All(q => q.Id != id)).Order(StringComparer.Ordinal));
        string Label(string id, string v) => questions.FirstOrDefault(q => q.Id == id)?.Options.FirstOrDefault(o => o.Value == v)?.Label ?? v;
        var parts = order.Where(answers.ContainsKey).Select(id => answers[id] switch
        {
            QuestionAnswer.Choice c => Label(id, c.Value),
            QuestionAnswer.Choices cs => string.Join(", ", cs.Values.Select(v => Label(id, v))),
            QuestionAnswer.Own own => w.Get("coach.answer.in_your_words", ("text", own.Text)),
            _ => "",
        }).ToList();
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}

/// <summary>
/// What's been picked on a questions card so far, as the iPad's <c>QuestionSelection</c>: a question's answer is one of its
/// choices (several, on a multi question) or the person's own words, never both. Opening "Something else…" clears the picks;
/// picking a choice closes the own field.
/// </summary>
public sealed class QuestionSelection
{
    readonly Dictionary<string, HashSet<string>> picked = [];
    readonly Dictionary<string, string> own = [];
    readonly HashSet<string> ownOpen = [];

    public bool IsPicked(CardQuestion q, string value) => picked.TryGetValue(q.Id, out var set) && set.Contains(value);
    public bool OwnOpen(CardQuestion q) => ownOpen.Contains(q.Id);
    public string OwnText(CardQuestion q) => own.GetValueOrDefault(q.Id, "");
    public void SetOwn(CardQuestion q, string text) => own[q.Id] = text;

    public void ToggleOwn(CardQuestion q)
    {
        if (!ownOpen.Remove(q.Id))
        {
            ownOpen.Add(q.Id);
            picked[q.Id] = [];
        }
    }

    public void Pick(CardQuestion q, string value)
    {
        if (q.Multi)
        {
            var set = picked.GetValueOrDefault(q.Id) ?? [];
            if (!set.Remove(value)) set.Add(value);
            picked[q.Id] = set;
        }
        else picked[q.Id] = [value];
        ownOpen.Remove(q.Id);
    }

    /// <summary>The question's answer, if it has one: its own words when that field is open and not blank, else its choices in Coach's order.</summary>
    public QuestionAnswer? Answer(CardQuestion q)
    {
        if (ownOpen.Contains(q.Id))
        {
            var words = OwnText(q).Trim();
            return words.Length == 0 ? null : new QuestionAnswer.Own(words);
        }
        var chosen = q.Options.Select(o => o.Value).Where(v => IsPicked(q, v)).ToList();
        if (chosen.Count == 0) return null;
        return q.Multi ? new QuestionAnswer.Choices(chosen) : new QuestionAnswer.Choice(chosen[0]);
    }
}

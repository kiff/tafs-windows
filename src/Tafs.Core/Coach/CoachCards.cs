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
    IReadOnlyList<PlanStep>? Steps = null)
{
    public static readonly string[] Kinds = ["steps", "notice", "confirm", "questions", "approval", "sign_in", "result", "knob", "drive"];
}

/// <summary>
/// What sending a card's answer came to, as the iPad's <c>answer(_:_:)</c>: sent (the thread is read again); a card that
/// doesn't wait any more (409, read again); refused for want of the AI-data consent (the card stays as chosen); or not reached.
/// </summary>
public enum CardSent { Sent, ReadAgain, Consent, NotReached }

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
public sealed record CardAnswer(string State, string Words, DateTimeOffset? At);

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
        return new CardAnswer(Lenient.Text(a, "state") ?? "answered", Lenient.Text(a, "words") ?? "", Lenient.At(a, "at"));
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
        foreach (var text in (string[])["title", "text", "tone", "grant", "draft", "service", "yes", "no", "note", "waits", "taf_id"])
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
            Lenient.Text(c, "tone"), options, questions, Lenient.Text(c, "waits"), steps);
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
    /// </summary>
    public static CardReply? Of(CardAnswer recorded, CoachCard card)
    {
        if (recorded.State != "answered") return null;
        var words = recorded.Words;
        var at = words.IndexOf(" · it didn't go through: ", StringComparison.Ordinal);
        var failed = at < 0 ? null : "failed: " + words[(at + " · it didn't go through: ".Length)..];
        var text = card.Kind switch
        {
            "confirm" => card.Options?.FirstOrDefault(o => o.Label == words)?.Value ?? words,
            "approval" => failed ?? (words == "Declined" ? "declined" : "approved"),
            "sign_in" => failed ?? (words == "Signed in" ? "signed in" : "not now"),
            "drive" => words.ToLowerInvariant().StartsWith("approved", StringComparison.Ordinal) ? "approved" : "declined",
            _ => words,
        };
        return new CardReply(card.Kind, card.Id, text, words);
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

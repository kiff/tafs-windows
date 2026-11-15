using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Coach;

public enum ChatFrom { Owner, Taf }

/// <summary>One step a run took, as tafs cloud records it: its tool, and whether it failed or was refused back unmade.</summary>
public sealed record WorkStep(string Tool, bool Error = false, bool NotMade = false);

/// <summary>What a run did before it wrote its answer (the iPad's <c>TafsCloud.Work</c>): how long, and its steps.</summary>
public sealed record ChatWork(int Ms, IReadOnlyList<WorkStep> Steps);

/// <summary>
/// One message of a chat, as the iPad's <c>TafsCloud.ChatMessage</c>: who wrote it, when, in which session, and tafs cloud's
/// lines around a taf's words (where a run's result went, what it did, a connection it was refused).
/// </summary>
public sealed record ChatMessage(DateTimeOffset At, ChatFrom From, bool Kickoff, string Text, string Session, ChatWork? Work = null,
    string? ServerId = null, string? Answers = null, string? Delivered = null, string? Counted = null, IReadOnlyList<string>? Refused = null)
{
    /// <summary>
    /// The same message while its text grows: a taf's by its session and time; an owner's by tafs cloud's id, else by its
    /// words, as the iPad's <c>id</c>.
    /// </summary>
    public string Id => From == ChatFrom.Taf ? $"{Session}|{At.ToUnixTimeMilliseconds()}|taf|{(Kickoff ? "kickoff" : "message")}"
        : ServerId is { } id ? $"{Session}|owner|{id}" : $"{Session}|{At.ToUnixTimeMilliseconds()}|owner|{Text}";
}

/// <summary>How a task Coach handed a taf went (the iPad's <c>CoachLine</c>); <paramref name="Away"/> when kept while the person was away.</summary>
public sealed record CoachLine(DateTimeOffset At, string Taf, string Said, bool Away);

/// <summary>One item of the thread, in order: a message, or a card where it's placed.</summary>
public sealed record ThreadItem(ChatMessage? Message, ShownCard? Card)
{
    public DateTimeOffset At => Message?.At ?? Card!.FirstAt;
    public string Id => Message is { } m ? "m|" + m.Id : "c|" + Card!.Ref;
}

/// <summary>Coach's thread, as tafs cloud answers <c>…/conversation</c>: its messages, its cards, its lines, and the session at work.</summary>
public sealed record CoachThread(IReadOnlyList<ChatMessage> Messages, IReadOnlyList<CoachLine> Lines, string? Working,
    IReadOnlyList<ShownCard>? Cards = null)
{
    public IReadOnlyList<ShownCard> Shown => Cards ?? [];

    /// <summary>The plan, shown pinned above the thread rather than in it, as the iPad's <c>SetupThread.plan</c>.</summary>
    public ShownCard? Plan => Shown.LastOrDefault(c => c.Card.Kind == "steps");

    /// <summary>
    /// The thread from tafs cloud's answer, as the iPad's <c>setupThread(decoding:)</c>: a message without its time, sender,
    /// text or session fails the whole read, as the iPad's decoder does; a line it can't read is left out.
    /// </summary>
    public static CoachThread? Read(JsonElement answer)
    {
        var messages = new List<ChatMessage>();
        foreach (var m in Lenient.Array(answer, "messages"))
        {
            if (Message(m) is not { } message) return null;
            messages.Add(message);
        }
        if (Lenient.Field(answer, "messages") is not { ValueKind: JsonValueKind.Array }) return null;
        var lines = Lenient.Array(answer, "coach_lines")
            .Select(l => Lenient.At(l, "at") is { } at && Lenient.Text(l, "taf") is { } taf && Lenient.Text(l, "said") is { } said
                ? new CoachLine(at, taf, said, Lenient.Bool(l, "away")) : null)
            .OfType<CoachLine>().ToList();
        // A card the app can't read is left out, never the thread with it.
        var cards = ShownCard.Fold(Lenient.Array(answer, "components").Select(ShownCard.Read).OfType<ShownCard>());
        return new(messages, lines, Lenient.Text(Lenient.Field(answer, "working") ?? default, "session"), cards);
    }

    static ChatMessage? Message(JsonElement m)
    {
        if (Lenient.At(m, "at") is not { } at || Lenient.Text(m, "text") is not { } text || Lenient.Text(m, "session") is not { } session) return null;
        ChatFrom? from = Lenient.Text(m, "from") switch { "owner" => ChatFrom.Owner, "taf" => ChatFrom.Taf, _ => null };
        if (from is null) return null;
        var refused = Lenient.Field(m, "refused") ?? default;
        return new ChatMessage(at, from.Value, Lenient.Text(m, "kind") == "kickoff", text, session, Work(Lenient.Field(m, "work")),
            Lenient.Text(m, "id"), Lenient.Text(m, "answers"), Line(Lenient.Text(m, "delivered")), Line(Lenient.Text(m, "counted")),
            new[] { Lenient.Text(refused, "words"), Lenient.Text(refused, "why"), Lenient.Text(refused, "next") }.Select(Line).OfType<string>().ToList() is { Count: > 0 } r ? r : null);
    }

    /// <summary>A run's work; one the app can't read leaves the message without it, as the iPad's <c>try?</c>.</summary>
    static ChatWork? Work(JsonElement? w)
    {
        if (w is not { ValueKind: JsonValueKind.Object } o || Lenient.Int(o, "ms") is not { } ms) return null;
        var steps = new List<WorkStep>();
        foreach (var s in Lenient.Array(o, "steps"))
        {
            if (Lenient.Text(s, "tool") is not { } tool) return null;
            steps.Add(new WorkStep(tool, Lenient.Bool(s, "error"), Lenient.Bool(s, "not_made")));
        }
        return Lenient.Field(o, "steps") is { ValueKind: JsonValueKind.Array } ? new ChatWork(ms, steps) : null;
    }

    /// <summary>One of tafs cloud's lines as drawn: trimmed, none when blank.</summary>
    public static string? Line(string? words) => string.IsNullOrWhiteSpace(words) ? null : words.Trim();

    /// <summary>
    /// The thread in order, as the iPad's <c>SetupThread.items</c>: messages and cards as they happened, the plan left out,
    /// each once; a message before a card placed at its time. A card its run waits on comes after what Coach wrote with it.
    /// </summary>
    public IReadOnlyList<ThreadItem> Items =>
        Messages.Select(m => (Item: new ThreadItem(m, null), At: m.At))
            .Concat(Shown.Where(c => c.Card.Kind != "steps").Select(c => (Item: new ThreadItem(null, c), At: PlacedAt(c))))
            .Select((x, i) => (x.Item, x.At, i)).OrderBy(x => x.At).ThenBy(x => x.i).Select(x => x.Item)
            .DistinctBy(x => x.Id).ToList();

    /// <summary>
    /// Where a card goes, as the iPad's <c>placedAt</c>: a card its run waits on, after Coach's words with it in its
    /// conversation, up to the person's next message, or once answered up to its answer; any other card at its own time.
    /// </summary>
    DateTimeOffset PlacedAt(ShownCard c)
    {
        if (string.IsNullOrEmpty(c.Card.Waits)) return c.FirstAt;
        if (c.Answer is { At: null }) return c.FirstAt;
        var nextOwner = Messages.FirstOrDefault(m => m.From == ChatFrom.Owner && m.At > c.FirstAt)?.At ?? DateTimeOffset.MaxValue;
        var end = c.Answer?.At is { } answered && answered < nextOwner ? answered : nextOwner;
        return Messages.LastOrDefault(m => m.From == ChatFrom.Taf && m.Session == c.Session && m.At >= c.FirstAt && m.At < end)?.At ?? c.FirstAt;
    }

    /// <summary>
    /// The thread as drawn, as the iPad's <c>textBeforeCards</c> with nothing streaming, with the kinds of card this app draws
    /// so far: an answer to a card it draws reads once, folded into its card, so no bubble repeats it; one that didn't go
    /// through keeps its bubble, which says so. A card of another kind is left out, and its answer keeps its bubble.
    /// </summary>
    public IReadOnlyList<ThreadItem> Drawn(params string[] kinds) =>
        Items.Where(x => x.Card is { } card ? kinds.Contains(card.Card.Kind) : x.Message is not { From: ChatFrom.Owner } m
            || CardReply.Parse(m.Text) is not { Failed: false } a || !Shown.Any(c => c.Card.Id == a.Id && kinds.Contains(c.Card.Kind))).ToList();

    /// <summary>
    /// The card an answer for <paramref name="id"/> is for, as the iPad's <c>cardRef</c>: the one waiting under that id, since
    /// Coach can use an id again, else the latest. Its ref keys what's answered here; its conversation goes with the answer.
    /// </summary>
    public ShownCard? CardFor(string id) =>
        Shown.LastOrDefault(c => c.Card.Id == id && c.Answer is null) ?? Shown.LastOrDefault(c => c.Card.Id == id);

    /// <summary>The ref an answer for <paramref name="id"/> is kept under, as the iPad's <c>cardRef</c>.</summary>
    public string CardRef(string id) => CardFor(id)?.Ref ?? "#" + id;

    /// <summary>
    /// The person's latest answer to a card, as the iPad's <c>answer(to:)</c>: the one kiff/agents recorded on it, else one
    /// sent as a message since the card first appeared.
    /// </summary>
    public CardReply? AnswerTo(string id)
    {
        var shown = Shown.LastOrDefault(c => c.Card.Id == id);
        if (shown is { Answer: { } recorded } && CardReply.Of(recorded, shown.Card) is { } a) return a;
        var since = shown?.FirstAt ?? DateTimeOffset.MinValue;
        return Messages.AsEnumerable().Reverse().Where(m => m.From == ChatFrom.Owner && m.At >= since)
            .Select(m => CardReply.Parse(m.Text)).FirstOrDefault(r => r?.Id == id);
    }
}

/// <summary>Coach's thread read and drawn, as the iPad's <c>loadSetupThread</c>, <c>SetupChatView.thread</c> and <c>WorkLog</c>.</summary>
public static class CoachChat
{
    /// <summary>
    /// The thread of Coach's newest conversation, as the iPad's <c>loadSetupThread</c> at launch: Coach's id from
    /// <c>GET /v1/setup</c>, its conversations from <c>…/chats</c>, the newest by when it was updated, then that one's
    /// <c>…/conversation/{session}</c>, or the whole thread without a list. With no Coach, or <c>/v1/setup</c> unread, none
    /// and no error, as the iPad's <c>try?</c>; a list it can't read is no list. A conversation that can't be read, or that
    /// doesn't decode, says "The conversation couldn't be loaded:" and why, as the iPad's <c>setupError</c>. Coach's id, to answer its cards.
    /// </summary>
    public static async Task<(CoachThread? Thread, string? Error, string? Coach)> LoadAsync(WordBook w, Func<string, Task<CloudRead>> read)
    {
        var setup = await read("/v1/setup");
        if (setup.Answer is not { } s || Lenient.Text(Lenient.Field(s, "setup") ?? default, "id") is not { } coach) return (null, null, null);
        var id = CloudRead.PathPart(coach);
        var chats = await read($"/v1/tafs/{id}/chats");
        var newest = chats.Answer is { } c && Chats(c) is { } list ? list.MaxBy(x => x.Updated).Session : null;
        var thread = await read($"/v1/tafs/{id}/conversation" + (newest is null ? "" : "/" + CloudRead.PathPart(newest)));
        // A thread that doesn't decode: the iPad says the system's words for it; this app has its own.
        var why = thread.Answer is { } t ? (CoachThread.Read(t) is { } read2 ? null : w.Get("error.ours")) : thread.Why;
        return why is null ? (CoachThread.Read(thread.Answer!.Value), null, coach) : (null, w.Get("coach.conversation_not_loaded", ("why", why)), coach);
    }

    /// <summary>
    /// Coach's conversations, as the iPad's strict <c>[Chat]</c>: each needs its session and title, and its start and update
    /// times; one that lacks any fails the list, as the iPad's decoder, and the whole thread is read instead.
    /// </summary>
    static List<(string Session, DateTimeOffset Updated)>? Chats(JsonElement answer)
    {
        if (Lenient.Field(answer, "chats") is not { ValueKind: JsonValueKind.Array } chats) return null;
        var list = new List<(string, DateTimeOffset)>();
        foreach (var x in chats.EnumerateArray())
        {
            if (Lenient.Text(x, "session") is not { } session || Lenient.Text(x, "title") is null || Lenient.At(x, "started") is null
                || Lenient.At(x, "updated") is not { } updated) return null;
            list.Add((session, updated));
        }
        return list;
    }

    /// <summary>
    /// An owner's message in their own words, as the iPad's <c>ownerWords</c>: an answer to a card ("[approval a1] declined")
    /// reads as what they chose; a confirm's as its option's label, a questions card's as its answers, from the card.
    /// </summary>
    public static string OwnerWords(WordBook w, string text, IReadOnlyList<ShownCard>? cards = null)
    {
        if (CardReply.Parse(text) is not { } a) return text;
        var card = cards?.FirstOrDefault(c => c.Card.Id == a.Id)?.Card;
        return a.Kind switch
        {
            "confirm" => card?.Options?.FirstOrDefault(o => o.Value == a.Text)?.Label ?? a.Text,
            "approval" => a.Failed ? w.Get("coach.answer.approve_failed") : a.Text == "declined" ? w.Get("coach.answer.decline") : w.Get("approval.approve"),
            "sign_in" => a.Failed ? w.Get("coach.answer.sign_in_failed") : a.Text == "not now" ? w.Get("common.not_now") : w.Get("coach.answer.signed_in"),
            "drive" => a.Text == "declined" ? w.Get("common.not_now") : w.Get("approval.approve"),
            "questions" => QuestionAnswers.Words(w, a.Text, card?.Questions ?? []) ?? a.Text,
            _ => a.Text,
        };
    }

    /// <summary>
    /// What tafs cloud's answer to a card's answer comes to, in the iPad's order: sent only when it reads as the iPad's
    /// <c>CardAnswered</c> (its session, run and status), a 409 before the consent's code, anything else not reached.
    /// </summary>
    public static CardSent Sent(CloudRead r) =>
        r.Answer is { } a ? (Shape.Required(a, "session", Shape.Text) && Shape.Required(a, "run", Shape.Text) && Shape.Required(a, "status", Shape.Text)
            ? CardSent.Sent : CardSent.NotReached)
        : r.Status == 409 ? CardSent.ReadAgain
        : r.Code == "ai_consent_required" ? CardSent.Consent : CardSent.NotReached;

    /// <summary>
    /// A card's answer as tafs cloud takes it (<c>POST /v1/tafs/{id}/components/{card}/answer</c>), as the iPad's
    /// <c>answerCard</c> with <c>answerValue</c>: <c>{"answer": …, "session": …}</c>. For a questions card, its answers as
    /// chosen; none for an answer the card doesn't take.
    /// </summary>
    public static (string Path, byte[] Body)? AnswerRequest(string coach, ShownCard card, string text)
    {
        JsonNode? value = card.Card.Kind switch
        {
            "questions" => QuestionAnswers.Decode(text) is not null ? JsonNode.Parse(text) : null,
            _ => null,
        };
        if (value is null) return null;
        var body = new JsonObject { ["answer"] = value, ["session"] = card.Session };
        return ($"/v1/tafs/{CloudRead.PathPart(coach)}/components/{CloudRead.PathPart(card.Card.Id)}/answer",
            System.Text.Encoding.UTF8.GetBytes(body.ToJsonString()));
    }

    /// <summary>
    /// A time between parts of the thread an hour or more apart, not on every line, as the iPad's <c>gapTime</c>: today's at
    /// "14:05", an earlier one with its date.
    /// </summary>
    public static string? GapTime(WordBook w, IReadOnlyList<ThreadItem> items, int i, DateTimeOffset now, TimeZoneInfo zone)
    {
        var at = items[i].At;
        if (i > 0 && at - items[i - 1].At < TimeSpan.FromHours(1)) return null;
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var culture = CultureInfo.GetCultureInfo(w.Language == "pt" ? "pt-PT" : w.Language);
        return local.Date == TimeZoneInfo.ConvertTime(now, zone).Date
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("d MMM yyyy, HH:mm", culture);
    }

    /// <summary>What Coach is doing, in the person's words, from the tool it calls, as the iPad's <c>activityWords</c>; none for "show".</summary>
    public static string? ActivityWords(WordBook w, string tool) => tool.Split("__")[^1] switch
    {
        "planque" => w.Get("coach.activity.connection"),
        "kiff_draft" => w.Get("coach.activity.card"),
        "kiff_card" or "kiff_pending" => w.Get("coach.activity.kiff"),
        "tafs_state" => w.Get("coach.activity.tafs"),
        "taf_run" => w.Get("coach.activity.run"),
        "taf_task" => w.Get("coach.activity.task"),
        "run_stop" => w.Get("coach.activity.stop"),
        "taf_pause" => w.Get("coach.activity.pause"),
        "version_try" => w.Get("coach.activity.try_version"),
        "version_install" => w.Get("coach.activity.install_version"),
        "memory" => w.Get("taf.activity.reading_notes"),
        "show" => null,
        _ => w.Get("chat.working"),
    };
}

public enum StepStatus { Running, Done, Error, Held, NotMade }

/// <summary>One row of steps: a step, or the tries of the same step one after another, with the last try's outcome.</summary>
public sealed record WorkRow(string Words, StepStatus Status, int Tries);

/// <summary>What a run did, folded under its answer as "Worked for 12 s", as the iPad's <c>WorkLog</c> and <c>WorkSummary</c>.</summary>
public static class WorkLog
{
    /// <summary>
    /// The steps as rows, as the iPad's <c>WorkLog.rows</c>: tries of the same step after a failed or unmade one are one row,
    /// with the last try's outcome. A failed row is left out once something worked, or while it's still at work; when
    /// nothing worked, the failures say why.
    /// </summary>
    public static IReadOnlyList<WorkRow> Rows(IReadOnlyList<(string Words, StepStatus Status)> steps, bool running)
    {
        var groups = new List<List<(string Words, StepStatus Status)>>();
        foreach (var s in steps)
        {
            if (groups.Count > 0 && groups[^1][0].Words == s.Words && groups[^1][^1].Status is StepStatus.Error or StepStatus.NotMade)
                groups[^1].Add(s);
            else groups.Add([s]);
        }
        var rows = groups.Select(g => new WorkRow(g[0].Words, g[^1].Status, g.Count)).ToList();
        return running || rows.Any(r => r.Status == StepStatus.Done) ? rows.Where(r => r.Status != StepStatus.Error).ToList() : rows;
    }

    /// <summary>"12 s", "1 min 5 s", as the iPad's <c>duration</c>: at least a second.</summary>
    public static string Duration(WordBook w, int ms)
    {
        var s = Math.Max(1, (int)Math.Round(ms / 1000.0, MidpointRounding.AwayFromZero));
        return s < 60 ? w.Get("work.duration.seconds", ("seconds", s.ToString(CultureInfo.InvariantCulture)))
            : w.Get("work.duration.minutes", ("minutes", (s / 60).ToString(CultureInfo.InvariantCulture)), ("seconds", (s % 60).ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Coach's finished work, as its <c>WorkSummary(coach: true)</c>: its steps in Coach's words, those it showed left out
    /// (never "Asking KIFF to run planque").
    /// </summary>
    public static IReadOnlyList<WorkRow> CoachRows(WordBook w, ChatWork work) =>
        Rows(work.Steps.Where(s => CoachChat.ActivityWords(w, s.Tool) is not null)
            .Select(s => (CoachChat.ActivityWords(w, s.Tool)!, s.NotMade ? StepStatus.NotMade : s.Error ? StepStatus.Error : StepStatus.Done)).ToList(), running: false);

    /// <summary>A step's words without the trailing "…" they have while it runs.</summary>
    public static string Past(string words) => words.EndsWith('…') ? words[..^1] : words;
}

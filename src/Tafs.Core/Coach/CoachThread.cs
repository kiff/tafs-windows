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
/// Where a message came from through one of a taf's doors, as the iPad's <c>TafsCloud.DoorMark</c>: its app ("email"), who
/// wrote it and about what; on the taf's answer, how it went back (<c>sent</c>, <c>not_sent</c> with why, or <c>pending</c>).
/// </summary>
public sealed record ChatDoor(string App, string? From = null, string? Subject = null, string? Reply = null, string? Why = null)
{
    /// <summary>The app's name as the doors say it, as the iPad's <c>DoorNames.label</c>: "email", "WhatsApp", else capitalised.</summary>
    public static string Label(string app) => app switch
    {
        "email" => "email",
        "whatsapp" => "WhatsApp",
        _ => app.Length == 0 ? app : char.ToUpperInvariant(app[0]) + app[1..],
    };

    /// <summary>
    /// Its stamp, as the iPad's <c>DoorStamp</c>: over the message, "BY EMAIL · who"; under the taf's answer, "↩ SENT BACK BY
    /// EMAIL · SAME THREAD", "NOT SENT BACK · why", or "GOES BACK BY EMAIL WHEN DONE".
    /// </summary>
    public string Stamp(WordBook w, bool fromOwner)
    {
        var app = Label(App).ToUpperInvariant();
        if (fromOwner) return w.Get("door.stamp.by", ("app", app)) + (From is { } from ? " · " + from : "");
        return Reply switch
        {
            "sent" => w.Get("door.stamp.sent_back", ("app", app)),
            "not_sent" => Why is { } why ? w.Get("door.stamp.not_sent_back") + " · " + why : w.Get("door.stamp.not_sent_back"),
            _ => w.Get("door.stamp.goes_back", ("app", app)),
        };
    }

    /// <summary>The stamp is in the failure colour when it wasn't sent back, else blue.</summary>
    public bool NotSent => Reply == "not_sent";
}

/// <summary>A file the person attached to a message, as the thread names it (the iPad's <c>AttachmentRef</c>): never its bytes.</summary>
public sealed record ChatFile(string Id, string Kind, string MediaType, string Name, int Size);

/// <summary>
/// One message of a chat, as the iPad's <c>TafsCloud.ChatMessage</c>: who wrote it, when, in which session, and tafs cloud's
/// lines around a taf's words (where a run's result went, what it did, a connection it was refused); where it came from
/// through a door, and the files sent with it.
/// </summary>
public sealed record ChatMessage(DateTimeOffset At, ChatFrom From, bool Kickoff, string Text, string Session, ChatWork? Work = null,
    string? ServerId = null, string? Answers = null, string? Delivered = null, string? Counted = null, IReadOnlyList<string>? Refused = null,
    ChatDoor? Door = null, IReadOnlyList<ChatFile>? Files = null)
{
    /// <summary>
    /// tafs cloud's lines around a taf's words, as the chat draws them (the iPad's <c>runLines</c>): where the run's result went
    /// above, what it did under. A person's message and a run's start have none.
    /// </summary>
    public (string? Above, string? Under) RunLines => From == ChatFrom.Taf && !Kickoff ? (Delivered, Counted) : (null, null);

    /// <summary>A refused connection's lines under a taf's words (the iPad's <c>refusedLines</c>): none on a person's message or a run's start.</summary>
    public IReadOnlyList<string>? RefusedLines => From == ChatFrom.Taf && !Kickoff ? Refused : null;

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
            new[] { Lenient.Text(refused, "words"), Lenient.Text(refused, "why"), Lenient.Text(refused, "next") }.Select(Line).OfType<string>().ToList() is { Count: > 0 } r ? r : null,
            Door(Lenient.Field(m, "door")), Files(Lenient.Field(m, "attachments")));
    }

    /// <summary>
    /// A door's mark, as the iPad's <c>try? DoorMark</c>: its app a string, each other field a string or absent; one it can't
    /// read leaves the message a plain one.
    /// </summary>
    static ChatDoor? Door(JsonElement? d)
    {
        if (d is not { ValueKind: JsonValueKind.Object } o || Lenient.Field(o, "app") is not { ValueKind: JsonValueKind.String } app) return null;
        var keys = new[] { "from", "subject", "reply", "why" };
        var fields = new string?[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            if (Lenient.Field(o, keys[i]) is not { } v || v.ValueKind == JsonValueKind.Null) continue;
            if (v.ValueKind != JsonValueKind.String) return null;
            fields[i] = v.GetString();
        }
        return new ChatDoor(app.GetString()!, fields[0], fields[1], fields[2], fields[3]);
    }

    /// <summary>
    /// The files sent with it, as the iPad's <c>try? [AttachmentRef]</c>: each with its id, kind, media type, name and whole
    /// size; one that can't be read leaves the message with none.
    /// </summary>
    static IReadOnlyList<ChatFile>? Files(JsonElement? a)
    {
        if (a is not { ValueKind: JsonValueKind.Array } list) return null;
        var files = new List<ChatFile>();
        foreach (var f in list.EnumerateArray())
        {
            string? Text(string key) => Lenient.Field(f, key) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
            if (Text("id") is not { } id || Text("kind") is not { } kind || Text("media_type") is not { } type || Text("name") is not { } name
                || Lenient.Field(f, "size") is not { ValueKind: JsonValueKind.Number } size || !size.TryGetInt32(out var bytes)) return null;
            files.Add(new ChatFile(id, kind, type, name, bytes));
        }
        return files.Count > 0 ? files : null;
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
    /// Where a new taf's hand-over stands, as the iPad's <c>handOver(_:listed:)</c>, by the card's ref: set up (tafs cloud
    /// answered its taf, or it's on the list already), why it wasn't (Coach hands it over again), or still setting up.
    /// </summary>
    public (HandOverState State, string? Why) HandOver(string cardRef, bool listed)
    {
        var outcome = Shown.FirstOrDefault(c => c.Ref == cardRef)?.Answer?.Outcome;
        if (outcome?.Failed is { } why) return (HandOverState.Failed, why);
        return (outcome?.TafId is not null || listed ? HandOverState.SetUp : HandOverState.SettingUp, null);
    }

    /// <summary>
    /// Whether Home's list should be read again for a hand-over, as the iPad's "the list is being read again for it": a
    /// result tafs cloud set up (its taf's id answered) whose taf isn't listed yet. Once per card: its ref goes into
    /// <paramref name="asked"/>, so a list that still lacks it isn't read again and again.
    /// </summary>
    public bool ReadHomeFor(IEnumerable<string?> listedAgents, ISet<string> asked)
    {
        var listed = listedAgents.OfType<string>().ToHashSet();
        var wanting = Shown.Where(c => c.Card is { Kind: "result", Taf: { Improves: null } t } && !listed.Contains(t.Agent)
            && HandOver(c.Ref, listed: false).State == HandOverState.SetUp).Select(c => c.Ref).Where(asked.Add).ToList();
        return wanting.Count > 0;
    }

    /// <summary>tafs cloud's words when the taf was made with no plan, so no run started, as the iPad's <c>planNeeded</c>.</summary>
    public string? PlanNeeded(string cardRef) => Shown.FirstOrDefault(c => c.Ref == cardRef)?.Answer?.Outcome?.PlanNeeded;

    /// <summary>What became of an improvement, as the iPad's <c>improvement</c>: the version made, why none was, or still preparing.</summary>
    public (ImprovementState State, int Version, string? Why) Improvement(string cardRef)
    {
        var outcome = Shown.FirstOrDefault(c => c.Ref == cardRef)?.Answer?.Outcome;
        if (outcome?.Failed is { } why) return (ImprovementState.Failed, 0, why);
        return outcome?.Version is { } n ? (ImprovementState.Made, n, null) : (ImprovementState.Preparing, 0, null);
    }

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

public enum HandOverState { SetUp, Failed, SettingUp }

public enum ImprovementState { Made, Failed, Preparing }

/// <summary>What the panel read: the thread, else why not; Coach's id; and the conversation's session, when known.</summary>
public sealed record CoachLoad(CoachThread? Thread, string? Error, string? Coach, string? Session);

/// <summary>Coach's thread read and drawn, as the iPad's <c>loadSetupThread</c>, <c>SetupChatView.thread</c> and <c>WorkLog</c>.</summary>
public static class CoachChat
{
    /// <summary>
    /// The thread of Coach's newest conversation, as the iPad's <c>loadSetupThread</c> at launch: Coach's id from
    /// <c>GET /v1/setup</c>, its conversations from <c>…/chats</c>, the newest by when it was updated, then that one's
    /// <c>…/conversation/{session}</c>, or the whole thread without a list. With no Coach, or <c>/v1/setup</c> unread, none
    /// and no error, as the iPad's <c>try?</c>; a list it can't read, or an empty one, is no list. A conversation that can't be read, or that
    /// doesn't decode, says "The conversation couldn't be loaded:" and why, as the iPad's <c>setupError</c>. Coach's id, to answer its cards.
    /// </summary>
    public static async Task<(CoachThread? Thread, string? Error, string? Coach)> LoadAsync(WordBook w, Func<string, Task<CloudRead>> read)
    {
        var loaded = await LoadAsync(w, read, CoachConversation.Newest);
        return (loaded.Thread, loaded.Error, loaded.Coach);
    }

    /// <summary>
    /// The conversation shown, as the iPad's <c>loadSetupThread</c>: the newest when none is chosen (above), the one the
    /// person reopened by its session, with no list read, or, for a new one, nothing read past Coach's id, since it has
    /// nothing until its first message. Also the session it is, for the next message, when the list or the choice names it.
    /// </summary>
    public static async Task<CoachLoad> LoadAsync(WordBook w, Func<string, Task<CloudRead>> read, CoachConversation shown)
    {
        var setup = await read("/v1/setup");
        if (setup.Answer is not { } s || Lenient.Text(Lenient.Field(s, "setup") ?? default, "id") is not { } coach) return new(null, null, null, null);
        if (shown.Fresh) return new(null, null, coach, null);
        var session = shown.Session;
        if (session is null)
        {
            var chats = await read(CoachChats.ListPath(coach));
            session = chats.Answer is { } c && CoachChats.Read(c) is { Count: > 0 } list ? list[0].Session : null;
        }
        var thread = await read($"/v1/tafs/{CloudRead.PathPart(coach)}/conversation" + (session is null ? "" : "/" + CloudRead.PathPart(session)));
        // A thread that doesn't decode: the iPad says the system's words for it; this app has its own.
        var decoded = thread.Answer is { } t ? CoachThread.Read(t) : null;
        var why = thread.Answer is null ? thread.Why : decoded is null ? w.Get("error.ours") : null;
        return why is null ? new(decoded, null, coach, session) : new(null, w.Get("coach.conversation_not_loaded", ("why", why)), coach, session);
    }

    /// <summary>
    /// The thread in the order already drawn, as Android's <c>Chat.steady</c> (kiff/tafs#1233): what's shown stays where it
    /// is, something new goes after it in the order read, and anything gone from the thread goes. With nothing drawn yet
    /// (the first read, or another conversation), the thread as read, so a reopened conversation shows a card after Coach's
    /// words. A card read again while Coach answers, or Coach's words joining at the end of a turn, then move nothing.
    /// </summary>
    public static IReadOnlyList<ThreadItem> Steady(IReadOnlyList<string>? drawn, IReadOnlyList<ThreadItem> read)
    {
        if (drawn is null || drawn.Count == 0) return read;
        var byId = read.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
        var kept = drawn.Distinct().Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        var shown = kept.Select(i => i.Id).ToHashSet();
        return [.. kept, .. read.Where(i => shown.Add(i.Id))];
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
    /// chosen; for an approval, its outcome; none for an answer the card doesn't take.
    /// </summary>
    public static (string Path, byte[] Body)? AnswerRequest(string coach, ShownCard card, string text)
    {
        JsonNode? value = card.Card.Kind switch
        {
            "questions" => QuestionAnswers.Decode(text) is not null ? JsonNode.Parse(text) : null,
            // A confirm's answer is its option's value, as answerValue's .text; an empty one isn't an answer.
            "confirm" => text.Length > 0 ? JsonValue.Create(text) : null,
            "approval" => ApprovalValue(text) is { } approval ? JsonValue.Create(approval) : null,
            _ => null,
        };
        if (value is null) return null;
        var body = new JsonObject { ["answer"] = value, ["session"] = card.Session };
        return ($"/v1/tafs/{CloudRead.PathPart(coach)}/components/{CloudRead.PathPart(card.Card.Id)}/answer",
            System.Text.Encoding.UTF8.GetBytes(body.ToJsonString()));
    }

    /// <summary>
    /// An approval's answer, as the iPad's <c>answerValue</c>: "approved" (whatever follows "approved,"), "declined", one
    /// signed earlier ("already approved: …") or "failed: why" as written; none for anything else.
    /// </summary>
    static string? ApprovalValue(string text) =>
        text == "approved" || text.StartsWith("approved,", StringComparison.Ordinal) ? "approved"
        : text == "declined" ? "declined"
        : text.StartsWith("already approved:", StringComparison.Ordinal) || text.StartsWith("failed:", StringComparison.Ordinal) ? text
        : null;

    /// <summary>"Refunds wasn't set up: why.", as the iPad's <c>notSetUp</c>: the why without its end's dots and spaces.</summary>
    public static string NotSetUp(WordBook w, string name, string why) => w.Get("coach.not_set_up", ("name", name), ("why", why.Trim('.', ' ')));

    /// <summary>"Refunds' new version wasn't made: why.", as the iPad's <c>SetupThread.notImproved</c>.</summary>
    public static string NotImproved(WordBook w, string name, string why) => w.Get("coach.not_improved", ("owner", name), ("why", why.Trim('.', ' ')));

    /// <summary>
    /// A tool's name in words, as the iPad's <c>KIFFMapping.toolWords</c>: the part after its server and dots, without a
    /// generated prefix ("k3f9a2_"), its underscores and dashes as spaces: "list_requests_k3f9" reads "list requests k3f9".
    /// </summary>
    public static string ToolWords(string tool)
    {
        // As Swift's split, empty parts are dropped; with none left, the tool's own name.
        var name = tool.Split("__")[^1].Split('.', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts ? parts[^1] : tool;
        if (System.Text.RegularExpressions.Regex.Match(name, "^(?=[a-z]*[0-9])[a-z0-9]{6,}_") is { Success: true } m && m.Length < name.Length)
            name = name[m.Length..];
        var words = string.Join(' ', name.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries));
        return words.Length == 0 ? tool : words;
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

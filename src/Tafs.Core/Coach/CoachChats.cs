using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>One of Coach's conversations, as the iPad's <c>TafsCloud.Chat</c>: named by what the person asked first.</summary>
public sealed record CoachChatRow(string Session, DateTimeOffset Started, DateTimeOffset Updated, string Title);

/// <summary>
/// Which of Coach's conversations the panel shows, as the iPad's <c>setupChat</c> and <c>setupFresh</c>: the newest (none
/// chosen, as at launch), one the person reopened, or a new one, which has nothing to read until its first message.
/// </summary>
public sealed record CoachConversation(string? Session, bool Fresh)
{
    public static readonly CoachConversation Newest = new(null, false);
    public static readonly CoachConversation New = new(null, true);
    public static CoachConversation Of(string session) => new(session, false);
}

/// <summary>
/// Coach's past conversations, as the iPad's <c>SetupChatsList</c> and <c>ArchivedSetupChats</c>: newest first, each
/// reopened with a tap; archived on tafs cloud, so it leaves the list on every device, and put back from Archived.
/// </summary>
public static class CoachChats
{
    public static string ListPath(string coach) => $"/v1/tafs/{CloudRead.PathPart(coach)}/chats";
    public static string ArchivedPath(string coach) => ListPath(coach) + "/archived";
    public static string ArchivePath(string coach, string session) => $"{ListPath(coach)}/{CloudRead.PathPart(session)}/archive";
    public static string PutBackPath(string coach, string session) => $"{ListPath(coach)}/{CloudRead.PathPart(session)}/unarchive";

    /// <summary>
    /// The list, as the iPad's strict <c>[Chat]</c>: each needs its session and title, and its start and update times; one
    /// that lacks any fails the list. Newest first, by when each was updated.
    /// </summary>
    public static IReadOnlyList<CoachChatRow>? Read(JsonElement answer)
    {
        if (Lenient.Field(answer, "chats") is not { ValueKind: JsonValueKind.Array } chats) return null;
        var list = new List<CoachChatRow>();
        foreach (var x in chats.EnumerateArray())
        {
            if (Lenient.Text(x, "session") is not { } session || Lenient.Text(x, "title") is not { } title
                || Lenient.At(x, "started") is not { } started || Lenient.At(x, "updated") is not { } updated) return null;
            list.Add(new(session, started, updated, title));
        }
        return list.OrderByDescending(c => c.Updated).ToList();
    }

    /// <summary>When a conversation was last updated, as the iPad's abbreviated date and short time: "15 Nov 2026, 09:00", in this PC's zone.</summary>
    public static string When(WordBook w, DateTimeOffset at, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("d MMM yyyy, HH:mm",
            Tafs.Core.Words.Locale.Culture(w.Language));

    /// <summary>
    /// A list read: its rows and tafs cloud's Delete words (<see cref="ChatDeleteWords"/>, none from a cloud that sends
    /// none), else why it couldn't be read ("The conversation couldn't be loaded:" and why).
    /// </summary>
    public static async Task<(IReadOnlyList<CoachChatRow>? Rows, string? Why, ChatDeleteWords? Delete)> ReadAsync(WordBook w, string path,
        Func<string, Task<CloudRead>> read)
    {
        var r = await read(path);
        if (r.Answer is { } a && Read(a) is { } rows) return (rows, null, ChatDeleteWords.Read(a));
        return (null, w.Get("coach.conversation_not_loaded", ("why", r.Answer is null ? r.Why ?? w.Get("error.offline") : w.Get("error.ours"))), null);
    }

    /// <summary>Archive: tafs cloud leaves it out of the list from then on. Not taken: "Not archived." and why.</summary>
    public static async Task<string?> ArchiveAsync(WordBook w, string coach, string session, Func<string, byte[], Task<CloudRead>> send) =>
        Why(w, await send(ArchivePath(coach, session), "{}"u8.ToArray()), "taf.not_archived", "coach.not_archived_try_again");

    /// <summary>Put back: the conversation returns to the list. Not taken: "Not put back." and why.</summary>
    public static async Task<string?> PutBackAsync(WordBook w, string coach, string session, Func<string, byte[], Task<CloudRead>> send) =>
        Why(w, await send(PutBackPath(coach, session), "{}"u8.ToArray()), "taf.not_put_back", "coach.not_put_back_try_again");

    /// <summary>Taken when tafs cloud answers <c>{archived}</c>; else its words for why, or the try-again line without any.</summary>
    static string? Why(WordBook w, CloudRead r, string withWhy, string without) =>
        r.Answer is { } a && Lenient.Field(a, "archived") is { ValueKind: JsonValueKind.True or JsonValueKind.False } ? null
        : r.Answer is null && r.Why is { } why ? w.Get(withWhy, ("why", why)) : w.Get(without);

    /// <summary>A conversation deleted for good: <c>DELETE /v1/tafs/{coach}/chats/{session}</c>.</summary>
    public static string DeletePath(string coach, string session) => $"{ListPath(coach)}/{CloudRead.PathPart(session)}";

    /// <summary>
    /// Delete, as the iPhone's <c>deleteSetupChat</c> and Android's <c>deleteCoachChat</c>: tafs cloud decides, the app follows
    /// its answer. Taken (2xx): it leaves the lists. 404, not this person's or already gone: it leaves them too, with nothing
    /// said. 409, its run works or waits on a card: it stays, with the refusal's own words. Any other failure, the network's
    /// included: it stays, with tafs cloud's <c>failed</c> line. Gone, what this PC kept of it goes too, as the iPhone's
    /// delete and as sign-out does: <paramref name="kept"/>, the folders of files opened from a chat and of the last chat
    /// export, each emptied as far as it can be (a file held open stays until sign-out).
    /// </summary>
    public static async Task<ChatDeleted> DeleteAsync(WordBook w, ChatDeleteWords words, string coach, string session,
        Func<string, Task<CloudRead>> delete, IReadOnlyList<string>? kept = null)
    {
        var r = await delete(DeletePath(coach, session));
        if (r.Answer is not null || r.Status is >= 200 and <= 299 || r.Status == 404)
        {
            foreach (var folder in kept ?? []) Account.ChatsExport.Remove(folder);
            return new(true, null);
        }
        if (r.Status == 409 && r.Why is { Length: > 0 } why && why != w.Get("error.cloud_answered", ("status", "409"))) return new(false, why);
        return new(false, words.Failed);
    }
}

/// <summary>What a delete did: <see cref="Gone"/> when it left the lists; else why it stays.</summary>
public sealed record ChatDeleted(bool Gone, string? Why);

/// <summary>
/// Delete's words, tafs cloud's (each chats-list read's <c>delete</c>): the item, the question by name, what goes and what stays,
/// its button, and the line when it wasn't deleted. All of them, or none: Delete shows only in tafs cloud's words, as the
/// iPhone's, with no stand-ins.
/// </summary>
public sealed record ChatDeleteWords(string Item, string ConfirmTitle, string ConfirmBody, string ConfirmButton, string Failed)
{
    public static ChatDeleteWords? Read(JsonElement answer)
    {
        if (Lenient.Field(answer, "delete") is not { ValueKind: JsonValueKind.Object } d) return null;
        string? T(string key) => Lenient.Text(d, key)?.Trim() is { Length: > 0 } t ? t : null;
        return T("item") is { } item && T("confirm_title") is { } title && T("confirm_body") is { } body
            && T("confirm_button") is { } button && T("failed") is { } failed ? new(item, title, body, button, failed) : null;
    }
}
/// <summary>
/// A message to Coach, as the iPad's <c>deliverSetupOnce</c> without a stream: into the conversation shown, or a new one
/// (<c>new_chat</c>); a conversation that already handed a taf over goes on in a new one (<c>new_chat_after_handover</c>),
/// which the answer names with <c>new_session</c>, and the panel follows it.
/// </summary>
public static class CoachSend
{
    public static string Path(string coach) => $"/v1/tafs/{CloudRead.PathPart(coach)}/messages";

    public static byte[] Body(string text, CoachConversation to, IReadOnlyList<string>? attachments = null, string? messageId = null)
    {
        var body = new Dictionary<string, object> { ["text"] = text };
        if (messageId is not null) body[Taf.TafChatSend.MessageIdField] = messageId;
        if (attachments is { Count: > 0 } ids) body["attachments"] = ids;
        if (to.Fresh) body["new_chat"] = true;
        else if (to.Session is { } session) body["session"] = session;
        body["new_chat_after_handover"] = true;
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    /// <summary>
    /// Sent: the conversation it went into, which the panel shows from then on. Not taken: why, kept in the thread as not sent
    /// (<see cref="Unsent"/>). An answer without its session isn't the iPad's <c>Sent</c>, so it reads as not taken.
    /// </summary>
    public static async Task<(string? Session, Refusal? Why)> SendAsync(WordBook w, string coach, string text, CoachConversation to,
        Func<string, byte[], Task<CloudRead>> send, IReadOnlyList<string>? attachments = null, string? messageId = null)
    {
        var r = await send(Path(coach), Body(text, to, attachments, messageId));
        if (r.Answer is { } a && Lenient.Text(a, "session") is { } session) return (session, null);
        return (null, r.Answer is null ? Refusal.Of(r, w.Get("error.offline")) : new Refusal(w.Get("error.ours")));
    }

    /// <summary>
    /// Improving a taf with Coach, as the iPad's <c>improveTaf</c>: the conversation's first message goes to the taf's own
    /// <c>POST /v1/tafs/{taf}/improve</c> with <c>{text}</c>, so Coach starts from what that taf did and what its Card allows,
    /// in a new conversation of its own; tafs cloud answers Coach's id and that conversation. Only the first message carries
    /// the taf; the rest go to Coach as any message does.
    /// </summary>
    public static string ImprovePath(string taf) => $"/v1/tafs/{CloudRead.PathPart(taf)}/improve";

    public static async Task<(string? Session, Refusal? Why)> ImproveAsync(WordBook w, string taf, string text, Func<string, byte[], Task<CloudRead>> send,
        IReadOnlyList<string>? attachments = null)
    {
        var r = await send(ImprovePath(taf), Taf.TafChatSend.Body(text, attachments));
        if (r.Answer is { } a && Lenient.Text(a, "session") is { } session) return (session, null);
        return (null, r.Answer is null ? Refusal.Of(r, w.Get("error.offline")) : new Refusal(w.Get("error.ours")));
    }
}

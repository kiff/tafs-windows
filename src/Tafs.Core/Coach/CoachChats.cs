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
            System.Globalization.CultureInfo.GetCultureInfo(w.Language == "pt" ? "pt-PT" : w.Language));

    /// <summary>A list read: its rows, else why it couldn't be read ("The conversation couldn't be loaded:" and why).</summary>
    public static async Task<(IReadOnlyList<CoachChatRow>? Rows, string? Why)> ReadAsync(WordBook w, string path, Func<string, Task<CloudRead>> read)
    {
        var r = await read(path);
        if (r.Answer is { } a && Read(a) is { } rows) return (rows, null);
        return (null, w.Get("coach.conversation_not_loaded", ("why", r.Answer is null ? r.Why ?? w.Get("error.offline") : w.Get("error.ours"))));
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
}

/// <summary>
/// A message to Coach, as the iPad's <c>deliverSetupOnce</c> without a stream: into the conversation shown, or a new one
/// (<c>new_chat</c>); a conversation that already handed a taf over goes on in a new one (<c>new_chat_after_handover</c>),
/// which the answer names with <c>new_session</c>, and the panel follows it.
/// </summary>
public static class CoachSend
{
    public static string Path(string coach) => $"/v1/tafs/{CloudRead.PathPart(coach)}/messages";

    public static byte[] Body(string text, CoachConversation to)
    {
        var body = new Dictionary<string, object> { ["text"] = text };
        if (to.Fresh) body["new_chat"] = true;
        else if (to.Session is { } session) body["session"] = session;
        body["new_chat_after_handover"] = true;
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    /// <summary>
    /// Sent: the conversation it went into, which the panel shows from then on. Not taken: "The message wasn't sent." and
    /// why. An answer without its session isn't the iPad's <c>Sent</c>, so it reads as not taken.
    /// </summary>
    public static async Task<(string? Session, string? Why)> SendAsync(WordBook w, string coach, string text, CoachConversation to,
        Func<string, byte[], Task<CloudRead>> send)
    {
        var r = await send(Path(coach), Body(text, to));
        if (r.Answer is { } a && Lenient.Text(a, "session") is { } session) return (session, null);
        return (null, w.Get("chat.not_sent", ("why", r.Answer is null ? r.Why ?? w.Get("error.offline") : w.Get("error.ours"))));
    }
}

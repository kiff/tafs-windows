using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>
/// Writing to a taf, as the iPad's <c>sendChat</c>, <c>SentMessages</c> and <c>stopChat</c>: the person's message goes to
/// tafs cloud (<c>POST /v1/tafs/{id}/messages</c>), shows at once, and stays on screen until the thread has it; the thread is
/// then read again while the taf works, where its answer arrives. Stop cancels the run at work. The streamed answer comes in
/// its own change.
/// </summary>
public static class TafChatSend
{
    /// <summary>The longest message, as the iPad's <c>chatLimit</c>.</summary>
    public const int Limit = 2000;

    /// <summary>How long a sent message waits on screen for the thread to have it, as the iPad's <c>SentMessages.kept</c>.</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromSeconds(120);

    /// <summary>How often the thread is read again while the taf works.</summary>
    public static readonly TimeSpan WhileWorking = TimeSpan.FromSeconds(2);

    public static string MessagesPath(string taf) => $"/v1/tafs/{CloudRead.PathPart(taf)}/messages";

    public static string StopPath(string taf) => $"/v1/tafs/{CloudRead.PathPart(taf)}/stop";

    public static byte[] Body(string text) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["text"] = text });

    /// <summary>
    /// A message with the files ready to go with it (<c>{text, attachments: [id]}</c>; words alone with none) and the id it's
    /// sent under (<c>client_message_id</c>), when it has one.
    /// </summary>
    public static byte[] Body(string text, IReadOnlyList<string>? attachments, string? messageId = null)
    {
        if (attachments is not { Count: > 0 } && messageId is null) return Body(text);
        var body = new Dictionary<string, object> { ["text"] = text };
        if (attachments is { Count: > 0 } ids) body["attachments"] = ids;
        if (messageId is not null) body[MessageIdField] = messageId;
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    /// <summary>
    /// The id a composed message is sent under (<c>client_message_id</c>, kiff/tafs docs/api.md › A taf's chat), as Android's:
    /// one UUID per message, the same on the stream, on the plain send that takes its place, on a steer, and on Retry, so
    /// tafs cloud acts on it once however often it arrives. A new or edited message gets a new one.
    /// </summary>
    public static string NewMessageId() => Guid.NewGuid().ToString("D");

    /// <summary>The message id's field, at the top level of the body.</summary>
    public const string MessageIdField = "client_message_id";

    public static byte[] StopBody(string session) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["session"] = session });

    /// <summary>
    /// Why a message isn't sent before anything goes: none when it's empty (nothing to send, nothing said), and "That's longer
    /// than 2000 characters…" past the limit, counted as the person sees characters.
    /// </summary>
    public static (string? Text, string? Why) Check(WordBook w, string typed)
    {
        var text = typed.Trim();
        if (text.Length == 0) return (null, null);
        if (new System.Globalization.StringInfo(text).LengthInTextElements > Limit)
            return (null, w.Count("chat.too_long", Limit));
        return (text, null);
    }

    /// <summary>
    /// When a message being sent is stamped, as the iPad's <c>SentMessages.stamp</c>: now, but never before the thread's
    /// latest, so a PC clock a little behind tafs cloud's doesn't place it above the answer it follows.
    /// </summary>
    public static DateTimeOffset Stamp(DateTimeOffset? latest, DateTimeOffset now) =>
        latest is { } l && l >= now ? l.AddMilliseconds(1) : now;

    /// <summary>The message as it shows the moment it's sent: the person's, in no session yet.</summary>
    public static ChatMessage Pending(string text, IReadOnlyList<ChatMessage> thread, DateTimeOffset now) =>
        new(Stamp(thread.Count > 0 ? thread.Max(m => m.At) : null, now), ChatFrom.Owner, false, text, "pending");

    /// <summary>
    /// The thread's messages with the sent ones it doesn't have yet, and those still waiting, as the iPad's
    /// <c>SentMessages.merge</c>. A sent message is matched once, to the person's own message with its id, else with its words
    /// at or after it was sent (less the time kept); one older than that is let go.
    /// </summary>
    public static (IReadOnlyList<ChatMessage> Messages, IReadOnlyList<ChatMessage> Waiting) Merge(IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatMessage> sent, DateTimeOffset now)
    {
        var taken = new HashSet<int>();
        var waiting = sent.Where(s =>
        {
            if (now - s.At > Kept) return false;
            for (var i = 0; i < messages.Count; i++)
            {
                var m = messages[i];
                if (taken.Contains(i) || m.From != ChatFrom.Owner) continue;
                var same = s.ServerId is { } id ? m.ServerId == id : m.Text == s.Text && m.At >= s.At - Kept;
                if (!same) continue;
                taken.Add(i);
                return false;
            }
            return true;
        }).ToList();
        return ([.. messages, .. waiting], waiting);
    }

    /// <summary>
    /// The message to tafs cloud, as the session. Not taken: why, in tafs cloud's words, else not reached; the thread keeps it
    /// as not sent with that line (<see cref="Unsent"/>).
    /// </summary>
    /// <param name="path">Another chat's path, sent to the same way: a trial's (<see cref="TafTrials.MessagesPath"/>).</param>
    public static async Task<Refusal?> SendAsync(WordBook w, string taf, string text, Func<string, byte[], Task<CloudRead>> send,
        IReadOnlyList<string>? attachments = null, string? path = null, string? messageId = null)
    {
        var answer = await send(path ?? MessagesPath(taf), Body(text, attachments, messageId));
        return answer.Answer is not null ? null : Refusal.Of(answer, w.Get("error.offline"));
    }

    /// <summary>
    /// Stop: the run at work in this session is cancelled (<c>POST /v1/tafs/{id}/stop</c>, for someone who manages the taf).
    /// Not taken: "Not stopped." and why.
    /// </summary>
    public static async Task<string?> StopAsync(WordBook w, string taf, string session, Func<string, byte[], Task<CloudRead>> send)
    {
        var answer = await send(StopPath(taf), StopBody(session));
        return answer.Answer is not null ? null : w.Get("chat.not_stopped", ("why", answer.Why ?? w.Get("error.offline")));
    }
}

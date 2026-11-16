using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>
/// A message that wasn't taken, kept where it was written, as the iPad's <c>UnsentMessage</c> (kiff/tafs#58, #476): its words,
/// why, and when, until Retry sends it again, Edit puts its words back in the box, or Delete drops it.
/// </summary>
public sealed record UnsentMessage(string Id, string Text, string Why, DateTimeOffset At)
{
    public static UnsentMessage Of(string text, string why, DateTimeOffset at) => new(Guid.NewGuid().ToString("N"), text, why, at);
}

/// <summary>The unsent bubble's words and its list, as the iPad's <c>UnsentBubble</c> and <c>retryChat</c>, the same in a taf's chat and Coach.</summary>
public static class Unsent
{
    /// <summary>"Not sent · Too busy.", under the faded bubble, in red.</summary>
    public static string Line(WordBook w, UnsentMessage m) => $"{w.Get("setup.not_sent")} · {m.Why}";

    /// <summary>Retry takes it out of the list, where it was; tried and not taken again, it's kept anew, at the end.</summary>
    public static (List<UnsentMessage> Left, int At) Take(IReadOnlyList<UnsentMessage> unsent, string id)
    {
        var at = unsent.ToList().FindIndex(m => m.Id == id);
        return (unsent.Where(m => m.Id != id).ToList(), at);
    }

    /// <summary>Not tried at all (nothing to send, too long, already sending): back in its place.</summary>
    public static List<UnsentMessage> PutBack(IReadOnlyList<UnsentMessage> unsent, UnsentMessage m, int at)
    {
        var list = unsent.ToList();
        list.Insert(Math.Clamp(at, 0, list.Count), m);
        return list;
    }
}

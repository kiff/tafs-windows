using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>
/// A message that wasn't taken, kept where it was written, as the iPad's <c>UnsentMessage</c> (kiff/tafs#58, #476): its words,
/// why, and when, until Retry sends it again, Edit puts its words back in the box, or Delete drops it.
/// </summary>
public sealed record UnsentMessage(string Id, string Text, string Why, DateTimeOffset At, string? Halted = null)
{
    public static UnsentMessage Of(string text, string why, DateTimeOffset at, string? halted = null) =>
        new(Guid.NewGuid().ToString("N"), text, why, at, halted);
}

/// <summary>Why a message wasn't taken: tafs cloud's words, and the halt it was refused for, when it was (<c>account</c> or <c>taf</c>).</summary>
public sealed record Refusal(string Why, string? Halted = null)
{
    public static Refusal Of(CloudRead r, string fallback) => new(r.Why ?? fallback, r.Halted);
}

/// <summary>The unsent bubble's words and its list, as the iPad's <c>UnsentBubble</c> and <c>retryChat</c>, the same in a taf's chat and Coach.</summary>
public static class Unsent
{
    /// <summary>"Not sent · Too busy.", under the faded bubble, in red.</summary>
    public static string Line(WordBook w, UnsentMessage m) => $"{w.Get("setup.not_sent")} · {m.Why}";

    /// <summary>
    /// Which Halt screen resumes what a refusal names, as the iPad's <c>UnsentBubble.halt</c>: the account's for a halted
    /// account (a Coach chat's included), the taf's own for a halted taf in its own chat. None otherwise: Retry can't pass a
    /// halt, so the bubble offers Resume, which opens that screen, where resuming is the person's act behind Windows Hello.
    /// </summary>
    public static HaltScreen? ResumesWith(UnsentMessage m, bool inTafChat) => m.Halted switch
    {
        "account" => HaltScreen.Account,
        "taf" when inTafChat => HaltScreen.Taf,
        _ => null,
    };

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

/// <summary>The Halt screen a halted refusal opens: the account's (Halt all), or the taf's own.</summary>
public enum HaltScreen { Account, Taf }

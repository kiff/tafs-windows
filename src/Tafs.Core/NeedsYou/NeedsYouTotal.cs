using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.NeedsYou;

/// <summary>
/// What waits for the person, as tafs cloud counts it on Home (<c>needs_you: {count, words}</c>, counted once so Home, the
/// sidebar and Needs you agree), as the iPad's <c>homeNeedsYou</c>, <c>needsYouCount</c> and <c>needsYouWords</c>.
/// </summary>
public sealed record NeedsYouTotal(int Count, string Words)
{
    /// <summary>Home's count; none when tafs cloud sent none (it leaves it out when it can't read everything in time).</summary>
    public static NeedsYouTotal? Read(JsonElement home) =>
        Lenient.Field(home, "needs_you") is { } n && Lenient.Int(n, "count") is { } count && count >= 0
            ? new(count, Lenient.Text(n, "words")?.Trim() ?? "") : null;

    /// <summary>
    /// The count shown: tafs cloud's, less the waiting Coach chats the app leaves out (open in Coach, or being set aside),
    /// else the app's own count of what it lists.
    /// </summary>
    public static int Shown(NeedsYouTotal? home, int hidden, int own) => home is { } h ? Math.Max(0, h.Count - hidden) : own;

    /// <summary>
    /// Its line: tafs cloud's words when nothing is left out, else the app's ("3 need you.", "Nothing needs you.").
    /// </summary>
    public static string Line(WordBook w, NeedsYouTotal? home, int hidden, int shown) =>
        hidden == 0 && home is { Words.Length: > 0 } h ? h.Words : shown == 0 ? w.Get("needs_you.nothing") : w.Count("needs_you.count", shown);

    /// <summary>The Overview's line for the rest, opening Needs you: only when more waits than the held calls it lists.</summary>
    public static bool ShowsRest(int shown, int held) => shown > held;
}

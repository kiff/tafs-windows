using System.Text.Json;
using Tafs.Core.Taf;

namespace Tafs.Core.NeedsYou;

/// <summary>A waiting Coach chat's words, every one tafs cloud's: what waits, the way in, and "not now" (none, no button).</summary>
public sealed record CoachWaitingWords(string Line, string Open, string SetAside);

/// <summary>
/// A Coach chat paused on a card its person answers (an approval, a sign-in, questions, a confirm), as Home lists it
/// (<c>coach_waiting</c>, kiff/tafs docs/api.md › Coach waiting) and the iPad's <c>TafsCloud.CoachWaiting</c>: the taf it's
/// on, the chat to open, and tafs cloud's words.
/// </summary>
public sealed record CoachWaiting(string Taf, string Session, string Run, string Since, CoachWaitingWords Words);

/// <summary>"Set all aside" (<c>coach_waiting_clear</c>): its button, and the one ask before it, whose title names the count.</summary>
public sealed record CoachWaitingClear(string Button, string Title, string Text, string Yes, string No);

/// <summary>
/// Needs you's waiting Coach chats, as the iPad's NeedsYouView draws them: each on its card, oldest first, with Open Coach
/// and, when tafs cloud sends its words, Set aside; "Set all aside" above them for two or more. Setting aside is tafs cloud's
/// "not now", never an approval: nothing is signed.
/// </summary>
public static class CoachWaitings
{
    /// <summary>
    /// Home's waiting chats, oldest first as tafs cloud lists them, once per chat. One without its chat or both of tafs cloud's
    /// lines (what waits and the way in) is left out, never Home with it.
    /// </summary>
    public static IReadOnlyList<CoachWaiting> Read(JsonElement home)
    {
        var seen = new HashSet<string>();
        var listed = new List<CoachWaiting>();
        foreach (var c in Lenient.Array(home, "coach_waiting"))
        {
            var w = Lenient.Field(c, "words") ?? default;
            var words = new CoachWaitingWords(T(w, "line"), T(w, "open"), T(w, "set_aside"));
            var waiting = new CoachWaiting(T(c, "taf"), T(c, "session"), T(c, "run"), T(c, "since"), words);
            if (waiting.Session.Length > 0 && words.Line.Length > 0 && words.Open.Length > 0 && seen.Add(waiting.Session)) listed.Add(waiting);
        }
        return listed;
    }

    /// <summary>"Set all aside" as tafs cloud sent it; none when it sent none, or not its button, the ask's title and its yes.</summary>
    public static CoachWaitingClear? ReadClear(JsonElement home)
    {
        var w = Lenient.Field(Lenient.Field(home, "coach_waiting_clear") ?? default, "words") ?? default;
        var clear = new CoachWaitingClear(T(w, "button"), T(w, "title"), T(w, "text"), T(w, "yes"), T(w, "no"));
        return clear is { Button.Length: > 0, Title.Length: > 0, Yes.Length: > 0 } ? clear : null;
    }

    /// <summary>
    /// The chats Needs you draws: Home's, less the one open in Coach now (the person is in it) and those being set aside, so a
    /// row that's been set aside never springs back while tafs cloud answers. Neither shows nor counts.
    /// </summary>
    public static IReadOnlyList<CoachWaiting> Shown(IReadOnlyList<CoachWaiting> listed, string? openInCoach, IReadOnlySet<string> settingAside) =>
        listed.Where(c => c.Session != openInCoach && !settingAside.Contains(c.Session)).ToList();

    /// <summary>Set aside on a row: signed in, with tafs cloud's words for it.</summary>
    public static bool CanSetAside(CoachWaiting waiting, bool signedIn) => signedIn && waiting.Words.SetAside.Length > 0;

    /// <summary>"Set all aside" above the rows: signed in, two or more shown, and tafs cloud sent it.</summary>
    public static CoachWaitingClear? SetAllAside(CoachWaitingClear? clear, int shown, bool signedIn) => signedIn && shown > 1 ? clear : null;

    /// <summary>Where one waiting chat is set aside, or every one with none: <c>POST /v1/home/coach-waiting/[{session}/]set-aside</c>.</summary>
    public static string SetAsidePath(string? session) =>
        "/v1/home/coach-waiting/" + (session is null ? "" : CloudRead.PathPart(session) + "/") + "set-aside";

    /// <summary>The chats tafs cloud set aside (<c>set_aside</c>), and its words for where they are now (<c>words.done</c>).</summary>
    public static (IReadOnlyList<string> Ended, string? Done) ReadSetAside(JsonElement answer) => (
        Lenient.Array(answer, "set_aside").Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()!).ToList(),
        Lenient.Text(Lenient.Field(answer, "words") ?? default, "done")?.Trim() is { Length: > 0 } done ? done : null);

    static string T(JsonElement o, string name) => Lenient.Text(o, name)?.Trim() ?? "";
}

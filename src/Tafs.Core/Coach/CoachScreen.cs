using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>Coach mode before a conversation starts: its title, the big heading, the line, Start and the footnote.</summary>
public sealed record CoachIntro(string Title, string Heading, string Line, string Start, string Footnote);

/// <summary>How Coach mode comes in and goes.</summary>
public enum CoachMotion { Slide, Fade }

/// <summary>
/// Coach mode, as on the iPad (its CoachPanel and SetupChatView): a panel from the right, a third of the window wide, over
/// what's open, which stays in view. Before Coach is started it shows the intro; Start asks for Windows Hello once.
/// </summary>
public static class CoachScreen
{
    /// <summary>
    /// How this PC asks the person, by its product's own name in every language, as the iPad names Face ID or Touch ID
    /// (KIFF's contract, §2.6: a Windows PC approves with Windows Hello).
    /// </summary>
    public const string Biometry = "Windows Hello";

    /// <summary>The panel opens at a third of the window, as the iPad's does.</summary>
    public static double PanelWidth(double window) => Math.Max(0, window) / 3;

    /// <summary>How long it takes to come in or go, or to change its width: the iPad's easeOut over 0.22 s.</summary>
    public static readonly TimeSpan MotionTime = TimeSpan.FromMilliseconds(220);

    /// <summary>It slides from the right; with Windows' animations off, it fades instead, as the iPad's does with Reduce Motion.</summary>
    public static CoachMotion Motion(bool animationsEnabled) => animationsEnabled ? CoachMotion.Slide : CoachMotion.Fade;

    public static CoachIntro Intro(WordBook w) => new(
        w.Get("coach.title"),
        w.Get("coach.coach_mode"),
        w.Get("coach.name_tool_taf_should_work", ("biometry", Biometry)),
        w.Get("coach.start"),
        w.Get("coach.starting_asks", ("biometry", Biometry)));
}

using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>Coach mode before a conversation starts: its title, the big heading, the line, Start and the footnote.</summary>
public sealed record CoachIntro(string Title, string Heading, string Line, string Start, string Footnote);

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

    public static CoachIntro Intro(WordBook w) => new(
        w.Get("coach.title"),
        w.Get("coach.coach_mode"),
        w.Get("coach.name_tool_taf_should_work", ("biometry", Biometry)),
        w.Get("coach.start"),
        w.Get("coach.starting_asks", ("biometry", Biometry)));
}

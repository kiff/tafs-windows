using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>Coach mode before a conversation starts: its title, the big heading, the line, Start and the footnote.</summary>
public sealed record CoachIntro(string Title, string Heading, string Line, string Start, string Footnote);

/// <summary>
/// The width the person chose for Coach mode, kept for the next time, as the iPad's <c>coach.width</c> and <c>coach.full</c>:
/// a share of the window, and whether it's full screen.
/// </summary>
public sealed record CoachWidth(double Share, bool Full)
{
    /// <summary>As it first opens: a third, not full screen.</summary>
    public static readonly CoachWidth Opening = new(CoachScreen.Narrowest, false);

    /// <summary>As kept in a file: the share, then 1 for full screen or 0.</summary>
    public string Kept() => FormattableString.Invariant($"{Share:R} {(Full ? 1 : 0)}");

    /// <summary>What was kept, read back; anything else, or a share outside a third to all, is the opening width.</summary>
    public static CoachWidth Read(string? kept)
    {
        var parts = (kept ?? "").Trim().Split(' ');
        return parts.Length == 2
            && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var share)
            && share >= CoachScreen.Narrowest - 1e-9 && share <= 1 && parts[1] is "0" or "1"
            ? new(share, parts[1] == "1") : Opening;
    }
}

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

    /// <summary>
    /// Whether "Taking shape" shows beside the conversation, as the iPad's <c>shaping</c>: from 780 across,
    /// the conversation and the column together; narrower, the conversation alone.
    /// </summary>
    public static bool Shaping(double width) => width >= ShapingFrom;

    public const double ShapingFrom = 780;

    /// <summary>The column's width, as the iPad's <c>SetupShapePanel().frame(width: 420)</c>.</summary>
    public const double ShapeWidth = 420;

    /// <summary>The panel opens at a third of the window, as the iPad's does.</summary>
    public static double PanelWidth(double window) => Math.Max(0, window) / 3;

    /// <summary>Never narrower than a third, the width it opens at, as the iPad's <c>narrowest</c>.</summary>
    public const double Narrowest = 1.0 / 3;

    /// <summary>Its width as chosen: full screen, or a share of the window, from a third to all of it.</summary>
    public static double PanelWidth(double window, CoachWidth chosen)
    {
        var w = Math.Max(0, window);
        return chosen.Full ? w : w * Math.Clamp(chosen.Share, Narrowest, 1);
    }

    /// <summary>While its edge is dragged (to the right, positive): wider or narrower, never past the window or below none.</summary>
    public static double Dragging(double window, CoachWidth chosen, double dragged) =>
        Math.Min(Math.Max(0, window), Math.Max(0, PanelWidth(window, chosen) - dragged));

    /// <summary>
    /// Where a drag of the edge ends, as the iPad's handle: more than 60 narrower than a third closes it (none); within 40
    /// of the window's width it's full screen, the share kept for Back to its width; else that share, at least a third.
    /// </summary>
    public static CoachWidth? Dragged(double window, CoachWidth chosen, double dragged)
    {
        if (window <= 0) return chosen;
        var to = PanelWidth(window, chosen) - dragged;
        if (to < window * Narrowest - 60) return null;
        if (to >= window - 40) return chosen with { Full = true };
        return new(Math.Clamp(to / window, Narrowest, 1), false);
    }

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

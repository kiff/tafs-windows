using Tafs.Core.NeedsYou;

namespace Tafs.App;

/// <summary>
/// What waits for the person, counted once, as the iPad's: tafs cloud's count on Home (<see cref="NeedsYouTotal"/>), less the
/// waiting Coach chats the app leaves out, else the app's own count. It's the sidebar's number, and the Overview's line for
/// what waits beyond the held calls it lists.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Home's count and words, as last read; none when tafs cloud sent none.</summary>
    NeedsYouTotal? homeNeedsYou;

    /// <summary>The waiting Coach chats Needs you leaves out: open in Coach, or being set aside.</summary>
    int NeedsYouHidden() => coachWaiting.Count - CoachWaitingShown().Count;

    /// <summary>The count shown: Home's less what's left out, else held calls, steps, "Did this happen?" and waiting chats.</summary>
    int NeedsYouShown() => NeedsYouTotal.Shown(homeNeedsYou, NeedsYouHidden(), held.Count + steps.Count + unproven.Count + CoachWaitingShown().Count);

    /// <summary>The Overview's line for the rest, opening Needs you; none when the held calls are all that waits.</summary>
    string? NeedsYouRest()
    {
        var shown = NeedsYouShown();
        return NeedsYouTotal.ShowsRest(shown, held.Count) ? NeedsYouTotal.Line(App.Words, homeNeedsYou, NeedsYouHidden(), shown) : null;
    }

    /// <summary>The count at Needs you's sidebar row, and with it the Overview's line for the rest.</summary>
    void ShowNeedsYouTotal()
    {
        if (needsYouRow is { } row) ShowCount(row, App.Words, NeedsYouTitle);
        else if (Pages.Content is OverviewPage overview) overview.ShowHeld(held, NeedsYouRest());
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// A screen pushed from Setup or the Card, as the iPad pushes it beside the sidebar: it takes the page's place, under a bar
/// with a round Back and its title in the middle, and the taf's head, tabs and Card bar go until Back brings them again.
/// </summary>
public sealed partial class TafPage
{
    /// <summary>The bar for a pushed screen's title; none brings the taf's head and its tabs (or a trial's bar) back.</summary>
    void ShowSubBar(string? title)
    {
        var pushed = title is not null;
        SubBar.Visibility = pushed ? Visibility.Visible : Visibility.Collapsed;
        Head.Visibility = pushed ? Visibility.Collapsed : Visibility.Visible;
        if (pushed)
        {
            SubTitle.Text = title!;
            AutomationProperties.SetName(SubBack, App.Words.Get("common.back"));
            TabsTrack.Visibility = TrialBar.Visibility = CardBar.Visibility = SideScroll.Visibility = Visibility.Collapsed;
            return;
        }
        var trial = opening?.InTrial is not null;
        TabsTrack.Visibility = trial ? Visibility.Collapsed : Visibility.Visible;
        TrialBar.Visibility = trial ? Visibility.Visible : Visibility.Collapsed;
        ShowSide();
    }

    /// <summary>Back: the screen it was pushed from, Setup or the Card.</summary>
    void OnSubBack(object sender, RoutedEventArgs e)
    {
        // A door goes back to Starts when, where it was opened from.
        if (tab == TafTab.Setup && setupScreen == SetupScreen.Door) OpenSetupScreen(SetupScreen.StartsWhen);
        else if (tab == TafTab.Setup && setupScreen != SetupScreen.Main) OpenSetupScreen(SetupScreen.Main);
        else
        {
            cardScreen = CardScreen.Main;
            ShowCards();
        }
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace Tafs.App;

public sealed partial class TafPage
{
    /// <summary>The page's parts hidden while its taf is gone, to show again if it comes back.</summary>
    readonly List<UIElement> hiddenForGone = [];
    bool gone;

    /// <summary>
    /// The taf on this page is no longer on Home's list (removed, here or elsewhere), as the iPad's <c>LateNote</c> in its place:
    /// nothing for a moment, while the window goes on, then "This taf is gone." fading in, dim, in the middle. Only if it's
    /// still gone then, so a Home read in passing never flashes it. On Home's list again, the page shows as it was.
    /// </summary>
    internal void ShowGone(bool isGone)
    {
        if (isGone == gone) return;
        gone = isGone;
        if (!isGone)
        {
            GoneNote.Visibility = Visibility.Collapsed;
            GoneNote.Opacity = 0;
            foreach (var part in hiddenForGone) part.Visibility = Visibility.Visible;
            hiddenForGone.Clear();
            return;
        }
        foreach (var part in Root.Children.Where(c => c != GoneNote && c.Visibility == Visibility.Visible).ToList())
        {
            part.Visibility = Visibility.Collapsed;
            hiddenForGone.Add(part);
        }
        GoneNote.Text = App.Words.Get("taf.gone");
        GoneNote.Opacity = 0;
        GoneNote.Visibility = Visibility.Visible;
        // 1.2 s, then 0.3 s in, as the iPad's.
        var fade = new DoubleAnimation { From = 0, To = 1, BeginTime = TimeSpan.FromSeconds(1.2), Duration = TimeSpan.FromSeconds(0.3) };
        Storyboard.SetTarget(fade, GoneNote);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var story = new Storyboard();
        story.Children.Add(fade);
        story.Begin();
    }
}

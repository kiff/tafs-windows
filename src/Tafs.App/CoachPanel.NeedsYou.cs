using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Tafs.Core.Coach;

namespace Tafs.App;

/// <summary>
/// The card that needs the person, when it's out of view, as the iPad's <c>NeedsYouBar</c> and <c>NeedsYouPill</c> (#58, #64):
/// a pinned pill over the thread, "Needs you · approve Stripe", that scrolls the card into view.
/// </summary>
public sealed partial class CoachPanel
{
    ShownCard? waitingCard;
    FrameworkElement? waitingElement;
    bool pillWired;

    /// <summary>Shows the pill while the waiting card is drawn and out of view; hides it otherwise.</summary>
    void ShowNeedsYouPill()
    {
        if (!pillWired)
        {
            pillWired = true;
            ThreadScroll.ViewChanged += (_, _) => ShowNeedsYouPill();
            ThreadScroll.SizeChanged += (_, _) => ShowNeedsYouPill();
        }
        var visible = false;
        if (waitingCard is { } card && waitingElement is { } element && element.XamlRoot is not null && ThreadView.Visibility == Visibility.Visible)
        {
            var top = element.TransformToVisual(ThreadScroll).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            var bottom = top + element.ActualHeight;
            visible = bottom < 0 || top > ThreadScroll.ViewportHeight;
            if (visible)
            {
                NeedsYouPillText.Text = CoachShape.NeedsYouBar(App.Words, card.Card, grants ?? []);
                AutomationProperties.SetName(NeedsYouPill, NeedsYouPillText.Text);
                AutomationProperties.SetHelpText(NeedsYouPill, App.Words.Get("coach.shows_it"));
            }
        }
        NeedsYouPill.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnNeedsYouPill(object sender, RoutedEventArgs e) =>
        waitingElement?.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.5, AnimationDesired = true });
}

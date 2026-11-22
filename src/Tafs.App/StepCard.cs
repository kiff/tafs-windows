using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tafs.Core.Approvals;
using Tafs.Core.NeedsYou;

namespace Tafs.App;

/// <summary>
/// Your step on its card, as the iPad's <c>YourStepCard</c>, every word tafs cloud's: its title as the eyebrow; the taf's name,
/// 14 semibold (not on the taf's own page, <c>onTaf</c>); what, 16 semibold; why, 14 soft; the vendor's page as an outlined
/// button only when tafs cloud vouched for its link, with its host under it, 12 mono dim; Done and Skip only when this PC can
/// answer, off while answering. Needs you and a taf's page draw it alike.
/// </summary>
static class StepCard
{
    /// <param name="onTaf">On the taf's own page: the card doesn't name it.</param>
    /// <param name="notReady">Windows Hello not ready: what's drawn in place of Done and Skip.</param>
    /// <param name="answer">Done (true) or Skip (false).</param>
    public static Border Build(YourStep step, bool onTaf, bool canAnswer, bool busy, HelloReadiness hello,
        Func<HelloNotReady, UIElement> notReady, Action<bool> answer)
    {
        var lines = new StackPanel { Spacing = 10 };
        if (step.Said.Title.Length > 0) lines.Children.Add(new TextBlock { Text = step.Said.Title, Style = Look("TafsEyebrow") });
        if (!onTaf && step.TafName is { Length: > 0 } name) lines.Children.Add(Line(name, 14, "TafsInkText", semibold: true));
        lines.Children.Add(Line(step.What, 16, "TafsInkText", semibold: true));
        if (step.Why.Length > 0) lines.Children.Add(Line(step.Why, 14, "TafsSoftText"));
        if (YourSteps.OpenLink(step) is { } link)
        {
            var open = new Button
            {
                Style = Look("TafsOutlineButton"),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { new FontIcon { Glyph = "", FontSize = 14 }, new TextBlock { Text = link.Words } },
                },
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, link.Words);
            open.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(link.Url);
            // Where it really goes, as tafs cloud read the link.
            var host = new TextBlock { Text = link.Host, FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Style = Look("TafsDimText") };
            lines.Children.Add(new StackPanel { Spacing = 4, Children = { open, host } });
        }
        // Answering confirms with Windows Hello: until it's ready, why, in place of Done and Skip, as the iPad's ApprovalNotReady.
        if (canAnswer && YourSteps.NotReady(App.Words, hello) is { } why) lines.Children.Add(notReady(why));
        else if (canAnswer)
        {
            // The iPad's PrimaryButtonStyle, and its look while off, as Coach's Send answers.
            var done = new Button { Content = step.Said.Done, Style = Look(busy ? "TafsPrimaryOff" : "TafsPrimary"), IsEnabled = !busy };
            var skip = new Button { Content = step.Said.Skip, Style = Look("TafsOutlineButton"), IsEnabled = !busy };
            done.Click += (_, _) => answer(true);
            skip.Click += (_, _) => answer(false);
            lines.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { done, skip } });
        }
        var card = new Border
        {
            Background = ChatViews.Themed("TafsPanel"), BorderBrush = ChatViews.Themed("TafsAmberEdge"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(16), Child = lines,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(card, "yourStep." + step.Handoff);
        return card;
    }

    /// <summary>
    /// Hello not ready, as the iPad's <c>ApprovalNotReady</c>: the line, then Open Settings (Windows' sign-in options) where
    /// setting Hello up is the way, and Check again.
    /// </summary>
    public static UIElement NotReady(HelloNotReady notReady, Action checkAgain)
    {
        var w = App.Words;
        var box = new StackPanel { Spacing = 8 };
        var line = Line(notReady.Line, 14, "TafsInkText");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(line, "approvalNotReady");
        box.Children.Add(line);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (notReady.OpensSettings)
        {
            var open = new Button { Content = w.Get("voice.open_settings"), Style = Look("TafsPrimary") };
            open.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(YourSteps.HelloSettings);
            buttons.Children.Add(open);
        }
        if (notReady.ChecksAgain)
        {
            var again = new Button { Content = w.Get("approval.check_again"), Style = Look("TafsOutlineButton") };
            again.Click += (_, _) => checkAgain();
            buttons.Children.Add(again);
        }
        if (buttons.Children.Count > 0) box.Children.Add(buttons);
        return box;
    }

    static TextBlock Line(string text, double size, string style, bool semibold = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Style = Look(style),
        FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
    };

    static Style Look(string key) => (Style)Application.Current.Resources[key];
}

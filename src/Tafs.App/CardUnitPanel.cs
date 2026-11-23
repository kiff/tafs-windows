using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// A Card unit's request, as the iPad's <c>CardUnitExceptionPanel</c>, on Needs you and on its taf's Setup: tafs cloud's title,
/// words and history; its Coach button (tafs cloud's <c>action_title</c>) for whoever manages the taf and may use Coach, which
/// opens Coach improving the taf with tafs cloud's words in the box, nothing sent; and Dismiss (its <c>dismiss_title</c>, in the
/// no colour), asked first, for someone who answers the account's holds, off while it goes. Why it wasn't dismissed under it.
/// </summary>
static class CardUnitPanel
{
    public static Border Build(CardUnitException ask, Action? discuss, Action? dismiss, bool dismissing, string? why)
    {
        var w = App.Words;
        var inside = new StackPanel { Spacing = 8 };
        inside.Children.Add(Text(ask.Title, 16, "TafsInkText", semibold: true));
        inside.Children.Add(Text(ask.Words, 14, "TafsSoftText"));
        if (ask.History.Length > 0) inside.Children.Add(Text(ask.History, 12, "TafsDimText"));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (discuss is not null && ask.ActionTitle.Length > 0)
        {
            var talk = new Button { Content = ask.ActionTitle, Style = Look("TafsOutlineButton") };
            AutomationProperties.SetAutomationId(talk, "cardUnit.coach." + ask.Id);
            talk.Click += (_, _) => discuss();
            buttons.Children.Add(talk);
        }
        if (dismiss is not null && ask.DismissTitle.Length > 0)
        {
            var no = new Button { Content = ask.DismissTitle, Style = Look("TafsOutlineNo"), IsEnabled = !dismissing };
            AutomationProperties.SetAutomationId(no, "cardUnit.dismiss." + ask.Id);
            no.Click += async (_, _) =>
            {
                // Asked first, as the iPad's .confirming(): Dismiss can't be undone.
                var sure = new ContentDialog
                {
                    XamlRoot = no.XamlRoot, RequestedTheme = no.ActualTheme, Title = w.Get("needs_you.card_unit.dismiss_prompt", ("title", ask.Title)),
                    PrimaryButtonText = ask.DismissTitle, CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
                };
                if (await sure.ShowAsync() == ContentDialogResult.Primary) dismiss();
            };
            buttons.Children.Add(no);
        }
        if (buttons.Children.Count > 0) inside.Children.Add(buttons);
        if (why is not null) inside.Children.Add(Text(why, 13, "TafsNoText"));
        var panel = new Border { Style = Look("TafsCard"), Child = inside };
        AutomationProperties.SetAutomationId(panel, "cardUnit." + ask.Id);
        return panel;
    }

    static TextBlock Text(string text, double size, string style, bool semibold = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Style = Look(style),
        FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
    };

    static Style Look(string key) => (Style)Application.Current.Resources[key];
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Kiff;

namespace Tafs.App;

/// <summary>
/// Account › Chrome's calls, as this device: the list (its status and planque's answer), and Unpair (why it didn't go through,
/// or none).
/// </summary>
sealed record ChromesCalls(Func<Task<(int Status, IReadOnlyList<PairedChrome>? Chromes, string? Why)>> Load, Func<PairedChrome, Task<string?>> Remove);

/// <summary>
/// Account › Chrome (docs/chrome.md), as the iPad's <c>ChromesView</c>: the Chromes paired to the account, each with what it
/// says it is and Unpair behind its confirm (planque deletes its key at once; the extension wipes itself at its next connect);
/// the footer; Pair a Chrome, which stays on the phone (it binds the approval key the extension later trusts), so here it says
/// to pair from a device that can approve; and the profile note.
/// </summary>
static class ChromesDialog
{
    static Style Look(string key) => (Style)Application.Current.Resources[key];

    static TextBlock Line(string text, double size, string style) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Style = Look(style) };

    public static async Task ShowAsync(XamlRoot root, ElementTheme theme, ChromesCalls calls)
    {
        var w = App.Words;
        var body = new StackPanel { Spacing = 12, MinWidth = 420 };
        var dialog = new ContentDialog
        {
            XamlRoot = root, RequestedTheme = theme, Title = w.Get("chrome.title"),
            Content = new ScrollViewer { Content = body, MaxHeight = 520 }, CloseButtonText = w.Get("common.done"),
        };
        string? said = null;
        string? pairSaid = null;

        async Task Draw()
        {
            body.Children.Clear();
            body.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left });
            var (_, chromes, why) = await calls.Load();
            body.Children.Clear();
            if ((said ?? (why is null ? null : w.Get("chrome.not_loaded", ("why", why)))) is { } line) body.Children.Add(Line(line, 14, "TafsNoText"));
            if (chromes is { Count: 0 }) body.Children.Add(Line(w.Get("chrome.none"), 15, "TafsDimText"));
            foreach (var chrome in chromes ?? [])
            {
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var words = new StackPanel { Spacing = 2 };
                var name = Line(chrome.Name, 15, "TafsInkText");
                name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                words.Children.Add(name);
                if (PlanqueChromes.Says(w, chrome) is { } says) words.Children.Add(Line(says, 13, "TafsDimText"));
                row.Children.Add(words);
                var unpair = new HyperlinkButton { Content = w.Get("chrome.remove"), Foreground = ChatViews.ThemeBrush(theme, "TafsNo"), VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(unpair, w.Get("chrome.remove_named", ("name", chrome.Name)));
                unpair.Click += (_, _) =>
                {
                    // Asked first, inside the same dialog: Windows shows one ContentDialog at a time.
                    var ask = new StackPanel { Spacing = 10 };
                    ask.Children.Add(Line(w.Get("chrome.remove_named", ("name", chrome.Name)), 15, "TafsInkText"));
                    var yes = new Button { Content = w.Get("chrome.remove"), Style = Look("TafsOutlineNo") };
                    var no = new Button { Content = w.Get("common.cancel"), Style = Look("TafsOutlineButton") };
                    ask.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { yes, no } });
                    var index = body.Children.IndexOf(row);
                    body.Children[index] = ask;
                    no.Click += (_, _) => body.Children[body.Children.IndexOf(ask)] = row;
                    yes.Click += async (_, _) =>
                    {
                        yes.IsEnabled = no.IsEnabled = false;
                        said = await calls.Remove(chrome);
                        await Draw();
                    };
                };
                Grid.SetColumn(unpair, 1);
                row.Children.Add(unpair);
                body.Children.Add(row);
            }
            body.Children.Add(Line(w.Get("chrome.footer"), 12, "TafsDimText"));
            var pair = new Button { Content = w.Get("chrome.pair"), Style = Look("TafsOutlineButton") };
            AutomationProperties.SetAutomationId(pair, "chrome.pair");
            pair.Click += (_, _) =>
            {
                // Pairing binds the approval key the extension trusts later: on the phone, as on Linux (the PM's 20:12).
                pairSaid = w.Get("approval.device_can_not_approve_approve");
                _ = Draw();
            };
            body.Children.Add(pair);
            if (pairSaid is { } p) body.Children.Add(Line(p, 13, "TafsAmberWords"));
            body.Children.Add(Line(w.Get("chrome.profile_note"), 12, "TafsDimText"));
        }

        _ = Draw();
        await dialog.ShowAsync();
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tafs.Core.SignIn;

namespace Tafs.App;

/// <summary>
/// The three cards on how tafs works (kiff/tafs <c>docs/onboarding.md</c>, the iPad's <c>OnboardingCards</c>): after the
/// celebration, once on this PC, and again from How tafs works. Each draws the app's own pieces with the sample's words, so
/// the person knows them a minute later: Home's taf rows, a Card's face, and Needs you's held call. Skip from any card;
/// Next, then Get started on the last.
/// </summary>
static class OnboardingCards
{
    /// <summary>Where this PC keeps that the cards were seen: after Skip or Get started they don't follow sign-in again.</summary>
    static readonly string SeenFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tafs", "cards-seen");

    /// <summary>Whether the cards follow this celebration: once on this PC.</summary>
    public static bool ShowAfterSignIn()
    {
        try { return Onboarding.ShowsCards(seen: File.Exists(SeenFile)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    static void MarkSeen()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SeenFile)!);
            File.WriteAllText(SeenFile, "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The cards over the window, from <paramref name="start"/>. After sign-in, finishing them marks them seen.</summary>
    public static async Task ShowAsync(XamlRoot root, ElementTheme theme, Onboarding.Card start = Onboarding.Card.Tafs, bool afterSignIn = false)
    {
        var dialog = new ContentDialog { XamlRoot = root, RequestedTheme = theme };
        dialog.Content = View(start, () =>
        {
            if (afterSignIn) MarkSeen();
            dialog.Hide();
        });
        // Escape is Skip, as on the iPad and Linux.
        dialog.Closing += (_, _) => { if (afterSignIn) MarkSeen(); };
        await dialog.ShowAsync();
    }

    /// <summary>
    /// For a screenshot (<c>--page cards-1</c>, 2 or 3): the same view on a panel over the window, since a dialog opens in a
    /// popup layer the window's picture doesn't hold.
    /// </summary>
    public static UIElement Preview(Onboarding.Card card) => new Grid
    {
        Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x66, 0, 0, 0)),
        Children =
        {
            new Border
            {
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(24), CornerRadius = new CornerRadius(8), Background = Brush("TafsBackground"),
                Child = View(card, () => { }),
            },
        },
    };

    /// <summary>The cards from <paramref name="start"/>: Skip, the picture, the title and lead, the dots, Next or Get started.</summary>
    static UIElement View(Onboarding.Card start, Action finish)
    {
        var w = App.Words;
        var card = start;

        var skip = new HyperlinkButton { Content = w.Get("onboarding.skip"), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(skip, "onboarding.skip");
        var picture = new Border { MinHeight = 230, HorizontalAlignment = HorizontalAlignment.Center };
        // A picture of the app, not a control: what it says is in the title and the lead.
        AutomationProperties.SetAccessibilityView(picture, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var title = new TextBlock { FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        var lead = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap, Style = Look("TafsSoftText") };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var next = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, Style = Look("TafsPrimary") };
        AutomationProperties.SetAutomationId(next, "onboarding.next");

        void Show()
        {
            picture.Child = card switch
            {
                Onboarding.Card.Tafs => TafRows(),
                Onboarding.Card.Card => TafPage.CardFace(Tafs.Core.Taf.TafScreen.SampleCards(w)[0], 0, w.Get("common.you"),
                    Onboarding.SampleTafRows(w)[0].Colour, revoked: false, Microsoft.UI.Colors.Gray),
                _ => HeldCall(),
            };
            title.Text = Onboarding.Title(w, card);
            lead.Text = Onboarding.Lead(w, card);
            next.Content = Onboarding.Button(w, card);
            dots.Children.Clear();
            foreach (var c in Enum.GetValues<Onboarding.Card>())
                dots.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = 8, Height = 8,
                    Fill = c == card ? Brush("TafsAmber") : Brush("TafsDim"), Opacity = c == card ? 1 : 0.4,
                });
        }

        skip.Click += (_, _) => finish();
        next.Click += (_, _) =>
        {
            if (Onboarding.Next(card) is { } after) { card = after; Show(); }
            else finish();
        };

        var column = new StackPanel { Spacing = 16, Width = 440 };
        column.Children.Add(skip);
        column.Children.Add(picture);
        column.Children.Add(title);
        column.Children.Add(lead);
        column.Children.Add(dots);
        column.Children.Add(next);
        Show();
        return column;
    }

    /// <summary>The first card: Home's rows for the sample's three tafs, each with its initial, colour, name and job.</summary>
    static UIElement TafRows()
    {
        var rows = new StackPanel { Spacing = 10, Width = 420 };
        foreach (var (name, job, colour) in Onboarding.SampleTafRows(App.Words))
        {
            var avatar = new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Background = RecordLineView.Colour(colour),
                Child = new TextBlock
                {
                    Text = Tafs.Core.NeedsYou.NeedsYouScreen.Initial(name), Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = name, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            words.Children.Add(new TextBlock { Text = job, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Style = Look("TafsSoftText") });
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(avatar);
            Grid.SetColumn(words, 1);
            row.Children.Add(words);
            rows.Children.Add(new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(14), Background = Brush("TafsRaised"), Child = row });
        }
        return rows;
    }

    /// <summary>The last card: Needs you's held call, the sample's Deploys asking, with its three answers shown, not live.</summary>
    static UIElement HeldCall()
    {
        var w = App.Words;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = w.Get("sample.deploys.name").ToUpper(Tafs.Core.Words.Locale.Culture(w.Language)), Style = Look("TafsEyebrow") });
        panel.Children.Add(new TextBlock { Text = w.Get("sample.deploys.deploy_api"), FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = w.Get("sample.deploys.deploy_api_why"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Style = Look("TafsSoftText") });
        panel.Children.Add(new TextBlock { Text = w.Get("needs_you.nothing_sent"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Style = Look("TafsDimText") });
        foreach (var (words, style) in new[] { (w.Get("needs_you.approve_once"), "TafsPrimary"), (w.Get("sample.deploys.next_step"), "TafsOutlineButton"), (w.Get("needs_you.refuse"), "TafsOutlineNo") })
            panel.Children.Add(new Button { Content = words, Style = Look(style), HorizontalAlignment = HorizontalAlignment.Stretch, IsHitTestVisible = false, IsTabStop = false });
        return new Border { Width = 420, Style = Look("TafsCard"), Child = panel, IsHitTestVisible = false };
    }

    static Style Look(string key) => (Style)Application.Current.Resources[key];

    /// <summary>A theme colour as the window shows it now (light or dark), never the app's default dictionary.</summary>
    static Brush Brush(string key) => ChatViews.Themed(key);
}

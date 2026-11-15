using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Account;
using Tafs.Core.SignIn;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>What Account opens with: whether the window shows the iPad's sample, and the theme it's in.</summary>
public sealed record AccountOpening(bool Sample, ElementTheme Theme);

/// <summary>
/// Account. Its rows follow where this PC stands (the sample, signed in with KIFF, or signed out). The KIFF account's name,
/// Signed in as and the plan come with KIFF's and tafs cloud's reads.
/// </summary>
public sealed partial class AccountPage : Page
{
    public static event Action? SignInChosen;
    public static event Action? SignedOut;
    public static event Action? AddThisDeviceChosen;
    public static event Action? ClearSampleChosen;
    public static event Action<ElementTheme>? ThemeChosen;
    public static event Action? AlertsChosen;

    AccountOpening opening = new(false, ElementTheme.Default);

    public AccountPage() => InitializeComponent();

    static Style Look(string key) => (Style)Application.Current.Resources[key];

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        opening = e.Parameter as AccountOpening ?? opening;
        Show();
    }

    void Show()
    {
        var w = App.Words;
        Heading.Text = w.Get("settings.title");
        while (Sections.Children.Count > 1) Sections.Children.RemoveAt(1);
        var kept = opening.Sample ? null : App.Sessions.Kept();
        var mode = AccountScreen.ModeOf(opening.Sample, kept);

        var account = new List<UIElement> { Row(w.Get("settings.mode"), AccountScreen.ModeWords(w, mode)) };
        // View only exactly when the session says view: Add this Windows PC pairs it.
        if (AccountScreen.ViewOnly(mode, kept))
        {
            account.Add(Row(w.Get("pair.view_only.title"), ""));
            account.Add(Action(w.Get("pair.view_only.add", ("device", w.Get("device.windows"))), "TafsAmberWords", () => AddThisDeviceChosen?.Invoke()));
        }
        Section(w.Get("settings.your_account"), account);
        Section(null, [Opens(Row(w.Get("settings.alerts"), "", chevron: true), w.Get("settings.alerts"), () => AlertsChosen?.Invoke())]);
        Section(w.Get("settings.theme_title"), [ThemePicker(w)]);
        Section(w.Get("settings.language_title"),
        [
            Row(w.Get("settings.language_title"), AccountScreen.LanguageName(w.Language)),
            // Windows' own setting: the app follows Windows' languages.
            Action(w.Get("settings.change_language"), "TafsAmberWords", () => _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:regionlanguage"))),
        ]);
        Section(w.Get("sign_in.how_works"), [Words(AccountScreen.HowItWorks(w), "TafsSoftText", 15)]);
        Section(w.Get("settings.legal"),
        [
            Action(AccountScreen.Legal(w, "settings.terms"), "TafsAccentText", () => _ = Windows.System.Launcher.LaunchUriAsync(SignInScreen.Terms)),
            Action(AccountScreen.Legal(w, "settings.privacy_policy"), "TafsAccentText", () => _ = Windows.System.Launcher.LaunchUriAsync(SignInScreen.Privacy)),
        ]);
        Section(w.Get("settings.about"),
        [
            Row(w.Get("settings.version"), AccountScreen.VersionLabel(typeof(App).Assembly.GetName().Version)),
            Words(w.Get("home.tagline"), "TafsDimText", 17),
        ]);
        Section(w.Get("settings.account_actions"), AccountScreen.Actions(w, mode).Select(a => Action(a.Words,
            a.Destructive ? "TafsNoText" : "TafsAccentText", () => Do(a.Action))).ToList());
    }

    async void Do(AccountAction action)
    {
        switch (action)
        {
            case AccountAction.SignIn: SignInChosen?.Invoke(); break;
            case AccountAction.ClearSample: ClearSampleChosen?.Invoke(); break;
            case AccountAction.SignOut:
                var (title, body) = AccountScreen.SignOutQuestion(App.Words);
                var ask = new ContentDialog
                {
                    XamlRoot = XamlRoot, Title = title, Content = body, PrimaryButtonText = App.Words.Get("settings.sign_out"),
                    CloseButtonText = App.Words.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
                };
                if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
                try
                {
                    await App.Sessions.SignOutAsync();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Windows refused to forget the session: say so, with its code only, and stay here to try again.
                    await new ContentDialog
                    {
                        XamlRoot = XamlRoot, CloseButtonText = App.Words.Get("common.ok"),
                        Content = App.Words.Get("common.didnt_go_through", ("what", App.Words.Get("settings.sign_out")), ("why", SessionStore.Why(ex))),
                    }.ShowAsync();
                    return;
                }
                SignedOut?.Invoke();
                break;
        }
    }

    /// <summary>A section: its label as written, then its rows in one panel with a line between them.</summary>
    void Section(string? title, IReadOnlyList<UIElement> rows)
    {
        if (title is not null) Sections.Children.Add(new TextBlock { Text = title, Style = Look("TafsListHead") });
        var list = new StackPanel();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = new Border { Style = Look("TafsHourLine"), Child = rows[i], BorderThickness = new Thickness(0, i > 0 ? 1 : 0, 0, 0) };
            list.Children.Add(row);
        }
        Sections.Children.Add(new Border { Style = Look("TafsListPanel"), Child = list, Margin = new Thickness(0, title is null ? 14 : 0, 0, 0) });
    }

    /// <summary>A label and its value on the right, 17, as the iPad's LabeledContent; a chevron for a row that opens.</summary>
    static Grid Row(string label, string value, bool chevron = false)
    {
        var g = new Grid { MinHeight = 52, ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = label, FontSize = 17, VerticalAlignment = VerticalAlignment.Center, Style = Look("TafsInkText") });
        var right = chevron
            ? new TextBlock { Text = "", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), FontSize = 13, Style = Look("TafsDimText") }
            : new TextBlock { Text = value, FontSize = 17, Style = Look("TafsDimText") };
        right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }

    /// <summary>A row that opens a screen, as the iPad's NavigationLink: the whole row, with its chevron.</summary>
    static Button Opens(Grid row, string name, Action open)
    {
        var b = new Button { Content = row, Style = Look("TafsBox"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(b, name);
        b.Click += (_, _) => open();
        return b;
    }

    static TextBlock Words(string text, string style, double size) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Style = Look(style), Margin = new Thickness(0, 14, 0, 14),
    };

    /// <summary>A row that acts: its words in the accent, amber or red, across the row.</summary>
    static Button Action(string words, string style, Action act)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = words, FontSize = 17, Style = Look(style) },
            Style = Look("TafsBox"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            MinHeight = 52,
        };
        AutomationProperties.SetName(b, words);
        b.Click += (_, _) => act();
        return b;
    }

    /// <summary>Light, Dark, System: the iPad's segmented control.</summary>
    UIElement ThemePicker(WordBook w)
    {
        var grid = new Grid { ColumnSpacing = 2 };
        (string Key, ElementTheme Theme)[] themes = [("settings.theme.light", ElementTheme.Light), ("settings.theme.dark", ElementTheme.Dark), ("settings.theme.system", ElementTheme.Default)];
        foreach (var (key, theme) in themes)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var b = new Button { Content = w.Get(key), Style = Look(theme == opening.Theme ? "TafsSegmentChosen" : "TafsSegment") };
            b.Click += (_, _) => { opening = opening with { Theme = theme }; ThemeChosen?.Invoke(theme); Show(); };
            Grid.SetColumn(b, grid.Children.Count);
            grid.Children.Add(b);
        }
        return new Border { Style = Look("TafsSegmentTrack"), Margin = new Thickness(0, 12, 0, 12), Child = grid };
    }
}

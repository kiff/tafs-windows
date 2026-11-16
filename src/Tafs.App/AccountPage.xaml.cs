using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Account;
using Tafs.Core.SignIn;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>What Account opens with: whether the window shows the iPad's sample, the theme it's in, and tafs cloud's read as the session.</summary>
/// <param name="Write">A write to tafs cloud as the session (method, path, body): the consent and the suggestions setting.</param>
/// <param name="Manages">Whether this person manages the account's tafs (KIFF's <c>cards.manage</c>): who may change suggestions.</param>
public sealed record AccountOpening(bool Sample, ElementTheme Theme, Func<string, Task<CloudRead>>? Read = null,
    KiffAccount? Account = null, Func<string, string, byte[]?, Task<CloudRead>>? Write = null, bool Manages = false,
    Func<string, Task<(string? Zone, string? Why)>>? SetTimeZone = null, Func<Task<(Deleted Done, string? Why)>>? DeleteAccount = null);

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
    public static event Action? DevicesChosen;
    public static event Action? PeopleChosen;
    public static event Action? ConnectAppsChosen;
    public static event Action? UsageChosen;

    AccountOpening opening = new(false, ElementTheme.Default);
    // Export my chats, as the iPad's ExportChatsSection: preparing, why the last one failed, and the files it made.
    bool exporting;
    string? exportWhy;
    IReadOnlyList<string>? exported;

    public AccountPage() => InitializeComponent();

    static Style Look(string key) => (Style)Application.Current.Resources[key];

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        opening = e.Parameter as AccountOpening ?? opening;
        Show();
        _ = ReadSettingsAsync();
    }

    string? zoneWhy;
    bool zoneBusy;

    /// <summary>Use this PC's zone; taken, the window reads the account again and Account follows; not, why shows under it.</summary>
    async Task UseZoneAsync(Func<string, Task<(string? Zone, string? Why)>> set, string zone)
    {
        if (zoneBusy) return;
        var mine = opening;
        (zoneBusy, zoneWhy) = (true, null);
        var (_, why) = await set(zone);
        if (opening != mine) return;
        (zoneBusy, zoneWhy) = (false, why);
        Show();
    }

    // The AI-data consent and Coach's suggestions, as tafs cloud has them; none until read, and then their rows show.
    AiConsent? consent;
    Suggestions? suggestions;
    string? settingsWhy;
    bool settingsBusy;

    /// <summary>The consent and suggestions read as Account opens, as the iPad's <c>loadAIConsent</c> and <c>loadSuggestions</c>.</summary>
    async Task ReadSettingsAsync()
    {
        if (opening.Sample || opening.Read is not { } read || App.Sessions.Kept() is null) return;
        var mine = opening;
        var c = await read(AiConsent.Path);
        var s = await read(Suggestions.Path);
        if (opening != mine) return;
        consent = c.Answer is { } ca ? AiConsent.Read(ca) : null;
        suggestions = s.Answer is { } sa ? Suggestions.Read(sa) : null;
        Show();
    }

    /// <summary>A change to one of them: busy meanwhile; taken, tafs cloud's answer is drawn; not, why shows in red under it.</summary>
    async Task ChangeAsync(string method, string path, byte[]? body, Action<System.Text.Json.JsonElement> taken)
    {
        if (opening.Write is not { } write || settingsBusy) return;
        var mine = opening;
        (settingsBusy, settingsWhy) = (true, null);
        Show();
        var answer = await write(method, path, body);
        if (opening != mine) return;
        settingsBusy = false;
        if (answer.Answer is { } json) taken(json);
        else settingsWhy = answer.Why ?? App.Words.Get("error.ours");
        Show();
    }

    /// <summary>The KIFF account, once read: Your account's rows are drawn again with it.</summary>
    internal void ShowAccount(KiffAccount? account)
    {
        opening = opening with { Account = account };
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
        // The KIFF account, as the iPad's Your account and AccountView: its name, its id when it differs, who is signed in
        // (each said once), and its time zone, which ends a Card's day.
        if (mode == AccountMode.Kiff && opening.Account is { } kiff)
        {
            // Only what KIFF takes from this session: a view-only one is offered neither change.
            var offered = kiff.Offered(AccountScreen.ViewOnly(mode, kept));
            account.Add(Row(w.Get("settings.kiff_account"), kiff.Name));
            if (kiff.Slug != kiff.Name) account.Add(Row(w.Get("settings.account_id"), kiff.Slug));
            if (kiff.SignedInAs is { } email) account.Add(Row(w.Get("settings.signed_in_as"), email));
            // Any zone, from tafs cloud's list, for a session KIFF lets change it, as the iPad's TimePicker; else the zone alone.
            if (offered.TimeZone && opening.SetTimeZone is { } choose && opening.Read is { } readZones)
                account.Add(Choose(w.Get("settings.time_zone_title"), kiff.TimeZone ?? "UTC", () => _ = PickZoneAsync(choose, readZones, kiff.TimeZone ?? "UTC")));
            else account.Add(Row(w.Get("settings.time_zone_title"), kiff.TimeZone ?? "UTC"));
            // This PC's zone, when the account's differs, as the iPad's "Use this iPad: …": one Windows Hello, then the device
            // key signs KIFF's change. Holds' deadlines, Activity, the Record and what tafs write use it.
            if (offered.TimeZone && opening.SetTimeZone is { } set && AccountTimeZone.ThisPc(TimeZoneInfo.Local) is { } here && here != (kiff.TimeZone ?? "UTC"))
                account.Add(Action(w.Get("settings.use_device_zone", ("device", w.Get("device.windows")), ("identifier", here)), "TafsAmberWords",
                    () => _ = UseZoneAsync(set, here)));
            if (offered.Delete && opening.DeleteAccount is { } delete)
                account.Add(Action(w.Get("settings.delete_account.row"), "TafsNoText", () => _ = DeleteAccountAsync(delete)));
            if (zoneWhy is { } why) account.Add(new TextBlock { Text = why, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 0, 20, 12), Style = Look("TafsNoText") });
        }
        // View only exactly when the session says view: Add this Windows PC pairs it.
        if (AccountScreen.ViewOnly(mode, kept))
        {
            account.Add(Row(w.Get("pair.view_only.title"), ""));
            account.Add(Action(w.Get("pair.view_only.add", ("device", w.Get("device.windows"))), "TafsAmberWords", () => AddThisDeviceChosen?.Invoke()));
        }
        Section(w.Get("settings.your_account"), account);
        // People & apps, as the iPad's, for a KIFF account: People & groups, then Devices.
        if (mode == AccountMode.Kiff)
            Section(w.Get("settings.people_apps"),
            [
                Opens(Row(w.Get("people.title"), "", chevron: true), w.Get("people.title"), () => PeopleChosen?.Invoke()),
                Opens(Row(w.Get("settings.devices.title"), "", chevron: true), w.Get("settings.devices.title"), () => DevicesChosen?.Invoke()),
                Opens(Row(w.Get("connect.apps.title"), "", chevron: true), w.Get("connect.apps.title"), () => ConnectAppsChosen?.Invoke()),
            ]);
        // What a new Coach conversation offers, as tafs cloud keeps it: its words say what the switch does now. Only someone who
        // manages the account's tafs changes it, as the iPad's.
        if (mode == AccountMode.Kiff && suggestions is { } shown)
        {
            var toggle = new ToggleSwitch { IsOn = shown.Show, IsEnabled = opening.Manages && !settingsBusy, OnContent = "", OffContent = "" };
            AutomationProperties.SetName(toggle, w.Get("settings.show_suggestions"));
            AutomationProperties.SetAutomationId(toggle, "show-suggestions");
            toggle.Toggled += (_, _) => _ = ChangeAsync("PUT", Suggestions.Path, Suggestions.Body(toggle.IsOn), j => suggestions = Suggestions.Read(j) ?? suggestions);
            var lines = new StackPanel { Spacing = 2 };
            lines.Children.Add(new TextBlock { Text = w.Get("settings.show_suggestions"), FontSize = 15, Style = Look("TafsInkText") });
            if (shown.Words.Length > 0) lines.Children.Add(new TextBlock { Text = shown.Words, FontSize = 13, TextWrapping = TextWrapping.Wrap, Style = Look("TafsSoftText") });
            var row = new Grid { Padding = new Thickness(20, 12, 20, 12), ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
            row.Children.Add(lines);
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);
            Section(w.Get("coach.title"), [row]);
        }
        // Plan, as the iPad's: Plan & usage, opening its screen.
        if (mode == AccountMode.Kiff)
            Section(w.Get("settings.plan"), [Opens(Row(w.Get("usage.title"), "", chevron: true), w.Get("usage.title"), () => UsageChosen?.Invoke())]);
        Section(null, [Opens(Row(w.Get("settings.alerts"), "", chevron: true), w.Get("settings.alerts"), () => AlertsChosen?.Invoke())]);
        Section(w.Get("settings.theme_title"), [ThemePicker(w)]);
        // The AI-data consent, as the iPad's AIConsentRow: given, its words and Withdraw in red; not given, its own screen. Every
        // word is tafs cloud's.
        if (mode == AccountMode.Kiff && consent is { Drawable: true } ai)
        {
            var rows = new List<UIElement>();
            if (ai.Given)
            {
                rows.Add(Row(ai.Words.Given, ""));
                if (ai.Words.Withdraw.Length > 0)
                    rows.Add(Action(ai.Words.Withdraw, "TafsNoText", () => _ = ChangeAsync("DELETE", AiConsent.Path, null, j => consent = AiConsent.Read(j) ?? consent)));
            }
            else rows.Add(Action(ai.Words.Title, "TafsAmberWords", () => _ = AskConsentAsync(ai)));
            if (settingsWhy is { } why) rows.Add(new TextBlock { Text = why, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 0, 20, 12), Style = Look("TafsNoText") });
            Section(null, rows);
        }
        Section(w.Get("settings.language_title"),
        [
            Row(w.Get("settings.language_title"), AccountScreen.LanguageName(w.Language)),
            // The app's own picker, as Android's (tafs#1214): this PC's language, then tafs's 8, each named in itself.
            Action(w.Get("settings.change_language"), "TafsAmberWords", () => _ = ChooseLanguageAsync()),
        ]);
        // Their own copy, for their rights of access and portability, for a KIFF account, as the iPad's.
        if (mode == AccountMode.Kiff)
        {
            Section(null, ExportRows(w));
            Footer(w.Get("export.own_chats_each_taf_coach"));
        }
        Section(w.Get("sign_in.how_works"), [Words(AccountScreen.HowItWorks(w), "TafsSoftText", 15)]);
        Section(w.Get("settings.legal"),
        [
            Action(AccountScreen.Legal(w, "settings.terms"), "TafsAccentText", () => _ = Windows.System.Launcher.LaunchUriAsync(SignInScreen.Terms(w.Language))),
            Action(AccountScreen.Legal(w, "settings.privacy_policy"), "TafsAccentText", () => _ = Windows.System.Launcher.LaunchUriAsync(SignInScreen.Privacy(w.Language))),
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
        // A section with no head sits as far below the one before as the iPad's inset-grouped list puts it (about 35).
        Sections.Children.Add(new Border { Style = Look("TafsListPanel"), Child = list, Margin = new Thickness(0, title is null ? 26 : 0, 0, 0) });
    }

    /// <summary>
    /// Export my chats (the iPad's <c>ExportChatsSection</c>): the row, with a spinner and "Preparing your chats…" while
    /// it's made, "Try again" after a failure; then why in red, or the files it made, to share again.
    /// </summary>
    List<UIElement> ExportRows(WordBook w)
    {
        var rows = new List<UIElement>();
        if (exporting)
        {
            var preparing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, MinHeight = 52 };
            preparing.Children.Add(new ProgressRing { IsActive = true, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center });
            preparing.Children.Add(new TextBlock { Text = w.Get("export.preparing"), FontSize = 17, VerticalAlignment = VerticalAlignment.Center, Style = Look("TafsDimText") });
            rows.Add(preparing);
            return rows;
        }
        rows.Add(Action(w.Get(exportWhy is null ? "export.row" : "common.try_again"), "TafsAccentText", Export));
        if (exportWhy is not null)
            rows.Add(new TextBlock { Text = exportWhy, FontSize = 13, TextWrapping = TextWrapping.Wrap, Style = Look("TafsNoText"), Margin = new Thickness(0, 12, 0, 12) });
        else if (exported is { } files)
        {
            var again = Action(ChatsExport.Ready(w, files), "TafsDimText", () => ShareFiles.Show(files, w.Get("export.row")));
            ((TextBlock)again.Content).FontSize = 13;
            ((TextBlock)again.Content).TextWrapping = TextWrapping.Wrap;
            rows.Add(again);
        }
        return rows;
    }

    /// <summary>As the iPad's <c>export</c>: one at a time; the share sheet opens with the files once they're made.</summary>
    async void Export()
    {
        if (exporting) return;
        var w = App.Words;
        exporting = true;
        exportWhy = null;
        Show();
        var read = opening.Read ?? (_ => Task.FromResult(CloudRead.Unreached(w)));
        var (files, why) = await ChatsExport.RunAsync(w, read, ChatsExport.Folder);
        exporting = false;
        exported = files ?? exported;
        exportWhy = why;
        Show();
        if (files is not null) ShareFiles.Show(files, w.Get("export.row"));
    }

    /// <summary>A section's footer, as the iPad's: 13, dim, under the panel.</summary>
    void Footer(string text) => Sections.Children.Add(new TextBlock
    {
        Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Style = Look("TafsDimText"), Margin = new Thickness(16, -4, 16, 0),
    });

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

    /// <summary>A row that opens a choice: its label, the value, and a chevron.</summary>
    static Button Choose(string label, string value, Action act)
    {
        var line = Row(label, value);
        var chevron = new TextBlock { Text = "\uE76C", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), FontSize = 13,
            Style = Look("TafsDimText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(chevron, 2);
        line.Children.Add(chevron);
        var b = new Button
        {
            Content = line, Style = Look("TafsBox"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(b, $"{label}: {value}");
        b.Click += (_, _) => act();
        return b;
    }

    /// <summary>
    /// The time zone, as the iPad's <c>TimeZonePicker</c>: "Use this Windows PC: …", then tafs cloud's list, searched there as
    /// the person types ("Find a time zone"), the account's marked. One chosen is set as this PC's is: Windows Hello, then the
    /// device key signs KIFF's change, then tafs cloud moves the tafs' schedules.
    /// </summary>
    async Task PickZoneAsync(Func<string, Task<(string? Zone, string? Why)>> set, Func<string, Task<CloudRead>> read, string current)
    {
        var w = App.Words;
        string? chosen = null;
        var search = new TextBox { PlaceholderText = w.Get("settings.find_time_zone") };
        AutomationProperties.SetName(search, w.Get("settings.find_time_zone"));
        var list = new StackPanel { Spacing = 0 };
        var problem = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Style = Look("TafsNoText"), Visibility = Visibility.Collapsed };
        var body = new StackPanel { Spacing = 10, MinWidth = 380 };
        ContentDialog? dialog = null;
        Button Zone(string id, string title)
        {
            var row = new Grid { MinHeight = 40, ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = title, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Style = Look("TafsInkText") });
            if (id == current)
            {
                var mark = new TextBlock { Text = "\uE73E", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), VerticalAlignment = VerticalAlignment.Center,
                    Style = Look("TafsAccentText") };
                Grid.SetColumn(mark, 1);
                row.Children.Add(mark);
            }
            var b = new Button { Content = row, Style = Look("TafsBox"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(b, title);
            if (id == current) AutomationProperties.SetItemStatus(b, "selected");
            b.Click += (_, _) => { chosen = id; dialog?.Hide(); };
            return b;
        }
        if (AccountTimeZone.ThisPc(TimeZoneInfo.Local) is { } here)
            body.Children.Add(Action(w.Get("settings.use_device_zone", ("device", w.Get("device.windows")), ("identifier", here)), "TafsAmberWords",
                () => { chosen = here; dialog?.Hide(); }));
        body.Children.Add(search);
        body.Children.Add(problem);
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 360 });
        var asked = 0;
        async Task LoadAsync(string q)
        {
            var mine = ++asked;
            var answer = await read(AccountTimeZone.ListPath(q));
            if (mine != asked) return;
            list.Children.Clear();
            if (answer.Answer is { } json && AccountTimeZone.Zones(json) is { } zones)
            {
                problem.Visibility = Visibility.Collapsed;
                foreach (var (id, title) in zones) list.Children.Add(Zone(id, title));
            }
            else
            {
                // The list wasn't read: said so, not that the zone didn't change, which nobody asked yet.
                problem.Text = w.Get("common.could_not_read_why", ("what", w.Get("settings.time_zone_title")), ("why", answer.Why ?? w.Get("error.ours")));
                problem.Visibility = Visibility.Visible;
            }
        }
        CancellationTokenSource? typing = null;
        search.TextChanged += async (_, _) =>
        {
            typing?.Cancel();
            typing = new CancellationTokenSource();
            var token = typing.Token;
            // A moment after the last key, as the iPad's 0.25 s.
            try { await Task.Delay(250, token); } catch (TaskCanceledException) { return; }
            await LoadAsync(search.Text);
        };
        dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = w.Get("settings.time_zone_title"), Content = body, CloseButtonText = w.Get("common.cancel"),
            RequestedTheme = ActualTheme,
        };
        _ = LoadAsync("");
        await dialog.ShowAsync();
        if (chosen is { } zone && zone != current) await UseZoneAsync(set, zone);
    }

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

    /// <summary>
    /// The language picker, as Android's: "Language", with "This Windows PC's language" first, then English, Deutsch,
    /// Français, Español, Italiano, Português, Nederlands and Polski, the current choice marked. Choosing one applies it at
    /// once (tafs starts again in it, still signed in); the one in use changes nothing.
    /// </summary>
    async Task ChooseLanguageAsync()
    {
        var w = App.Words;
        var kept = App.KeptLanguage();
        var list = new RadioButtons { MaxColumns = 1 };
        list.Items.Add(new RadioButton { Content = w.Get("settings.language_device", ("device", w.Get("device.windows"))), Tag = "" });
        foreach (var (code, name) in AppLanguage.Named)
        {
            var item = new RadioButton { Content = name, Tag = code };
            // Read in its own language by Narrator.
            item.Language = code;
            list.Items.Add(item);
        }
        list.SelectedIndex = kept is null ? 0 : 1 + AppLanguage.Named.ToList().FindIndex(l => l.Code == kept);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = w.Get("settings.language_title"), Content = new ScrollViewer { Content = list, MaxHeight = 440 },
            CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(dialog, "settings.language_picker");
        string? chosen = null;
        var picked = false;
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not RadioButton { Tag: string code }) return;
            (chosen, picked) = (code.Length == 0 ? null : code, true);
            dialog.Hide();
        };
        await dialog.ShowAsync();
        if (!picked) return;
        if (App.ChooseLanguage(chosen) is { } why) await new ContentDialog { XamlRoot = XamlRoot, Content = why, CloseButtonText = w.Get("common.ok") }.ShowAsync();
    }

    /// <summary>The consent's screen, as the iPad's <c>AIConsentView</c>: its title and lines, Allow (for this version) and Not now.</summary>
    async Task AskConsentAsync(AiConsent ai)
    {
        var lines = new StackPanel { Spacing = 10 };
        foreach (var line in ai.Words.Lines) lines.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = ai.Words.Title, Content = new ScrollViewer { Content = lines, MaxHeight = 420 },
            PrimaryButtonText = ai.Words.Allow, CloseButtonText = ai.Words.NotNow.Length > 0 ? ai.Words.NotNow : App.Words.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(dialog, "ai-consent");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await ChangeAsync("POST", AiConsent.Path, ai.AllowBody(), j => consent = AiConsent.Read(j) ?? consent);
    }

    /// <summary>
    /// Delete account, as the iPad's <c>DeleteAccountView</c>: its warning and what goes, confirmed with Windows Hello. Deleted,
    /// this PC forgets the session as a sign-out does and opens sign-in; not, why shows, and trying again goes on from where it
    /// stopped.
    /// </summary>
    async Task DeleteAccountAsync(Func<Task<(Deleted Done, string? Why)>> delete)
    {
        var w = App.Words;
        var lines = new StackPanel { Spacing = 8 };
        lines.Children.Add(new TextBlock { Text = w.Get("settings.delete_account.warning"), TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        lines.Children.Add(new TextBlock { Text = w.Get("settings.delete_account.what_goes"), Style = Look("TafsEyebrow") });
        foreach (var key in new[] { "settings.delete_account.goes_tafs", "settings.delete_account.goes_cards", "settings.delete_account.goes_sign_ins", "settings.delete_account.goes_records" })
            lines.Children.Add(new TextBlock { Text = "• " + w.Get(key), TextWrapping = TextWrapping.Wrap });
        lines.Children.Add(new TextBlock { Text = w.Get("settings.delete_account.confirm_with", ("biometry", Tafs.Core.Coach.CoachScreen.Biometry)), TextWrapping = TextWrapping.Wrap, Style = Look("TafsSoftText") });
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = w.Get("settings.delete_account.title"), Content = lines,
            PrimaryButtonText = w.Get("settings.delete_account.title"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(ask, "settings.delete_account");
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
        var (done, why) = await delete();
        if (done == Deleted.Yes)
        {
            // KIFF deleted the account and its sessions: forget this one here, as a sign-out does, whatever the logout answers.
            try { await App.Sessions.SignOutAsync(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            SignedOut?.Invoke();
            return;
        }
        if (done == Deleted.No && why is not null)
            await new ContentDialog { XamlRoot = XamlRoot, Content = why, CloseButtonText = w.Get("common.ok") }.ShowAsync();
    }
}

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Tafs.Core.Calendar;
using Tafs.Core.Connections;
using Tafs.Core.Home;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Record;
using Tafs.Core.Taf;
using Tafs.Core.Usage;
using Tafs.Core.Pairing;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace Tafs.App;

public sealed partial class MainWindow : Window
{
    /// <summary>A laptop's window: the size the PR screenshots are taken at.</summary>
    static readonly SizeInt32 LaptopSize = new(1280, 800);

    /// <summary>The held calls Needs you shows: from KIFF in their own change; until then none, or the iPad's samples.</summary>
    readonly IReadOnlyList<HeldCall> held;

    /// <summary>The Record's lines: from tafs cloud and KIFF in their own change; until then none, or the iPad's sample.</summary>
    readonly IReadOnlyList<RecordEntry> record;
    /// <summary>The Overview's lines: tafs cloud's once Home is read, else none, or the sample's.</summary>
    IReadOnlyList<OverviewRow> overviewRows;
    /// <summary>Home's answer from tafs cloud, for a taf's page; none until it's read.</summary>
    HomeAnswer? home;
    readonly bool sample;
    readonly UsageReads usage;
    readonly ConnectAppsList? connectApps;

    /// <summary>The view the Calendar opens on: Week; Day or Month for `--page calendar` or `calendar-month`.</summary>
    CalendarMode calendarOpensOn = CalendarMode.Week;

    /// <param name="page">A screen to open first: "pair" for Add this computer, "sign-in", "needs-you", "record", "calendar" (Day), "calendar-week", "calendar-month", "usage", "connect-apps", "account", "coach", or "taf" / "taf-setup" / "taf-card" (the sample's Refunds on its Activity, Setup or Card, with --sample). Otherwise the Overview.</param>
    /// <param name="viewOnly">The session can't approve yet: show the View only notice above every screen.</param>
    /// <param name="sample">Show the iPad's sample held calls and Record: for screenshots.</param>
    /// <param name="homeFile">Home's list from a contract file (contracts/home.json) in place of tafs cloud: for screenshots.</param>
    public MainWindow(string? screenshot, string? theme, string? page, bool viewOnly, bool sample = false, string? homeFile = null)
    {
        InitializeComponent();
        if (theme is "light" or "dark") Root.RequestedTheme = theme == "light" ? ElementTheme.Light : ElementTheme.Dark;
        this.sample = sample;
        var w = App.Words;
        Title = w.Get("app.name");
        AppWindow.Resize(LaptopSize);

        Brand.Text = w.Get("app.name");
        AutomationProperties.SetName(BrandItem, w.Get("home.overview.title"));
        held = sample ? NeedsYouScreen.Sample(w, DateTimeOffset.UtcNow) : [];
        record = sample ? RecordScreen.Sample(w, DateTimeOffset.UtcNow) : [];
        usage = sample ? new(UsageScreen.SampleBalance(w), UsageScreen.SampleCredits(w), UsageScreen.SampleCredits(w, previous: true)) : new(null, null, null);
        connectApps = sample ? ConnectAppsScreen.Sample() : null;
        overviewRows = sample ? OverviewScreen.SampleRows(w, DateTimeOffset.UtcNow) : [];
        var at = Nav.MenuItems.IndexOf(BrandItem) + 1;
        foreach (var item in Sidebar.Screens(w))
        {
            var row = new NavigationViewItem
            {
                Content = item.Title,
                Tag = item.Screen,
                Icon = new FontIcon { Glyph = item.Glyph, Style = (Style)Application.Current.Resources["TafsSidebarIcon"] },
                Style = (Style)Application.Current.Resources["TafsSidebarItem"],
            };
            if (item.Screen == Screen.NeedsYou) ShowCount(row, w, item.Title);
            Nav.MenuItems.Insert(at++, row);
        }
        AddHeldCalls(w);
        YourTafs.Content = Sidebar.Eyebrow(w, "home.your_tafs");
        // The tafs list comes from tafs cloud in its own change; until then none, or the sample's.
        var yourTafs = sample ? Sidebar.SampleYourTafs(w, DateTimeOffset.UtcNow) : [];
        AddYourTafs(yourTafs);
        NoTafs.Content = Sidebar.NoTafs(w);
        NoTafs.Visibility = yourTafs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var foot = Sidebar.Foot(w);
        CoachText.Text = foot.Coach;
        AutomationProperties.SetName(CoachButton, w.Get("coach.coach_mode"));
        AccountTitle.Text = foot.Account;
        AutomationProperties.SetName(AccountButton, foot.AccountLabel);

        var notice = PairingScreen.ViewOnly(w);
        ViewOnly.Title = notice.Title;
        ViewOnly.Message = notice.Lead;
        AddThisDevice.Content = notice.Add;
        // A kept session says how this PC approves: the notice shows exactly when it's view.
        ViewOnly.IsOpen = viewOnly || (screenshot is null && App.Sessions.Kept() is { Mode: ApprovalMode.View });

        SignInPage.PairInsteadChosen += OpenPairing;
        NeedsYouPage.AddThisDeviceChosen += OpenPairing;
        ConnectAppsPage.AskCoachChosen += () => OnCoach(this, new RoutedEventArgs());
        // Focus goes back to the sidebar's Coach when Coach mode has gone.
        CoachMode.Closed += () => CoachButton.Focus(FocusState.Programmatic);
        OverviewPage.TafChosen += key => OpenBeside(typeof(TafPage), OpenTaf(w, key));
        OverviewPage.NeedsYouChosen += () => Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.NeedsYou);
        SignInPage.SignedIn += session =>
        {
            ViewOnly.IsOpen = session.Mode == ApprovalMode.View;
            Nav.SelectedItem = BrandItem;
        };
        // Paired with a key in the TPM: this PC approves, with its own passkey or the account's. Kept in memory for now.
        PairingPage.Paired += session => ViewOnly.IsOpen = session.Mode == ApprovalMode.View;
        this.screenshot = screenshot;

        AccountPage.ThemeChosen += chosen => Root.RequestedTheme = chosen;
        AccountPage.SignInChosen += OpenSignIn;
        AccountPage.AlertsChosen += () => OpenBeside(typeof(ComingPage), w.Get("settings.alerts"));
        AccountPage.AddThisDeviceChosen += OpenPairing;
        AccountPage.SignedOut += () => { ViewOnly.IsOpen = false; OpenSignIn(); };
        // Leaving the sample: the app starts again without it, as the iPad's Clear the sample leaves it.
        AccountPage.ClearSampleChosen += () => Microsoft.Windows.AppLifecycle.AppInstance.Restart(
            string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Where(a => a != "--sample")));

        if (page == "pair") OpenPairing();
        else if (page == "account") OnAccount(this, new RoutedEventArgs());
        else if (page == "sign-in") OpenSignIn();
        else if (page is "calendar" or "calendar-week" or "calendar-month")
        {
            // The reference set's two Calendar screenshots: Day, and Week (what the Calendar opens on).
            calendarOpensOn = page switch { "calendar" => CalendarMode.Day, "calendar-month" => CalendarMode.Month, _ => CalendarMode.Week };
            Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.Calendar);
        }
        else if (page == "usage") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.Usage);
        else if (page == "coach")
        {
            // Over the Overview, which stays in view beside it.
            Nav.SelectedItem = BrandItem;
            CoachMode.Open(animate: false);
        }
        else if (page == "connect-apps") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.ConnectApps);
        else if (page == "record") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.Record);
        else if (page == "needs-you") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.NeedsYou);
        else if (page is "taf" or "taf-setup" or "taf-card" && sample)
            OpenBeside(typeof(TafPage), SampleTaf(w, page switch { "taf-setup" => TafTab.Setup, "taf-card" => TafTab.Card, _ => TafTab.Activity }));
        else Nav.SelectedItem = BrandItem;
        if (screenshot is not null) Root.Loaded += async (_, _) => await SaveAndQuitAsync(screenshot);
        if (homeFile is not null) ShowHome(HomeList.Read(w, Tafs.Core.Contracts.ContractFile.Answer(homeFile), TimeZoneInfo.Local));
        else if (screenshot is null && !sample && App.Sessions.Kept() is { } session) Root.Loaded += async (_, _) => await ReadHomeAsync(session.AccessToken);
    }

    /// <summary>
    /// Home's list from tafs cloud, as the session, once the window is up: the Overview's lines and Your tafs. An answer
    /// that can't be read leaves them as they were ("No tafs yet."). A screenshot never reads it.
    /// </summary>
    async Task ReadHomeAsync(string bearer)
    {
        var w = App.Words;
        HomeAnswer read;
        try
        {
            var (status, body) = await KiffHttp.GetAsync(new Uri(HomeList.Api, HomeList.Path), bearer, w.Language);
            if (status != 200) return;
            using var json = System.Text.Json.JsonDocument.Parse(body);
            read = HomeList.Read(w, json.RootElement, TimeZoneInfo.Local);
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or System.Text.Json.JsonException) { return; }
        ShowHome(read);
    }

    /// <summary>Home's answer on the Overview and under Your tafs, and for a taf's page.</summary>
    void ShowHome(HomeAnswer read)
    {
        var w = App.Words;
        home = read;
        overviewRows = HomeList.Rows(w, home);
        var yours = HomeList.YourTafs(w, home, DateTimeOffset.UtcNow);
        AddYourTafs(yours);
        // As the iPad's sidebar: the line for no tafs only with none at all, and with tafs but none relevant now, no Your
        // tafs: they're all on the Overview.
        NoTafs.Visibility = home.Tafs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        YourTafs.Visibility = home.Tafs.Count > 0 && yours.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (Pages.Content is OverviewPage overview) overview.ShowRows(overviewRows);
    }

    /// <summary>
    /// How many calls wait, at Needs you's row's end: a plain number in the soft colour, as the iPad's sidebar list
    /// draws a row's badge (not the tab bar's red pill). The row is named with it.
    /// </summary>
    void ShowCount(NavigationViewItem row, Tafs.Core.Words.WordBook w, string title)
    {
        var (count, name) = Sidebar.NeedsYouCount(w, held.Count);
        AutomationProperties.SetName(row, name);
        if (count is null) return;
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        var number = new TextBlock
        {
            Text = count, FontWeight = Microsoft.UI.Text.FontWeights.Normal, Margin = new Thickness(8, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["TafsSidebarCount"],
        };
        Grid.SetColumn(number, 1);
        line.Children.Add(number);
        row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        row.Content = line;
    }

    /// <summary>A taf's row in the sidebar: it opens the taf's page.</summary>
    sealed record TafRow(string Key);

    /// <summary>
    /// Under YOUR TAFS, the tafs relevant now, as the iPad's sidebar: the avatar (34), the name in 15 semibold, and what
    /// it's doing in 13 soft, on one line each. A row opens its taf's page.
    /// </summary>
    void AddYourTafs(IReadOnlyList<OverviewRow> rows)
    {
        var at = Nav.MenuItems.IndexOf(YourTafs) + 1;
        foreach (var taf in rows)
        {
            var content = new Grid { ColumnSpacing = 12 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.Children.Add(new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = RecordLineView.Colour(taf.Colour),
                Child = new TextBlock { Text = TafScreen.Initial(taf.Taf), FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var lines = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(new TextBlock { Text = taf.Taf, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            lines.Children.Add(new TextBlock { Text = taf.Now, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
                Style = (Style)Application.Current.Resources["TafsSoftText"] });
            Grid.SetColumn(lines, 1);
            content.Children.Add(lines);
            var row = new NavigationViewItem
            {
                Content = content, Tag = new TafRow(taf.Key), Style = (Style)Application.Current.Resources["TafsSidebarItem"],
                HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 56,
            };
            AutomationProperties.SetName(row, $"{taf.Taf}. {taf.Now}");
            Nav.MenuItems.Insert(at++, row);
        }
    }

    /// <summary>
    /// NEEDS YOU above YOUR TAFS, as on the iPad: each held call on one row, an amber dot, "Taf: what it asks", and
    /// "reason · answer by 00:27" under it. A row opens Needs you. With none, the section isn't there.
    /// </summary>
    void AddHeldCalls(Tafs.Core.Words.WordBook w)
    {
        if (held.Count == 0) return;
        var at = Nav.MenuItems.IndexOf(YourTafs);
        Nav.MenuItems.Insert(at++, new NavigationViewItemHeader
        {
            Content = Sidebar.Eyebrow(w, "needs_you.title"),
            Style = (Style)Application.Current.Resources["TafsSidebarHeaderAmber"],
        });
        foreach (var call in held)
        {
            var (title, line) = NeedsYouScreen.SidebarRow(w, call, TimeZoneInfo.Local);
            var lines = new StackPanel { Spacing = 2 };
            lines.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            lines.Children.Add(new TextBlock { Text = line, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
                Style = (Style)Application.Current.Resources["TafsSoftText"] });
            // An 8-point amber dot in the icons' column, and the words where the rows' names start.
            var content = new Grid { ColumnSpacing = 10 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TafsAmber"],
            });
            Grid.SetColumn(lines, 1);
            content.Children.Add(lines);
            var row = new NavigationViewItem
            {
                Content = content,
                Tag = call,
                Style = (Style)Application.Current.Resources["TafsSidebarItem"],
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                MinHeight = 52,
            };
            AutomationProperties.SetName(row, $"{title}. {line}");
            Nav.MenuItems.Insert(at++, row);
        }
    }

    void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        ShowAccountChosen(false);
        if (item == BrandItem)
            Pages.Navigate(typeof(OverviewPage), new OverviewOpening(held, overviewRows));
        else if (item.Tag is Screen.NeedsYou or HeldCall) Pages.Navigate(typeof(NeedsYouPage), held);
        else if (item.Tag is Screen.Record) Pages.Navigate(typeof(RecordPage), record);
        else if (item.Tag is Screen.Usage) Pages.Navigate(typeof(UsagePage), usage);
        else if (item.Tag is Screen.ConnectApps) Pages.Navigate(typeof(ConnectAppsPage), connectApps);
        else if (item.Tag is Screen.Calendar) Pages.Navigate(typeof(CalendarPage), calendarOpensOn);
        else if (item.Tag is TafRow taf) Pages.Navigate(typeof(TafPage), OpenTaf(App.Words, taf.Key));
        else Pages.Navigate(typeof(ComingPage), item.Content);
    }

    /// <summary>Coach mode slides in from the right, over what's open, which stays in view.</summary>
    void OnCoach(object sender, RoutedEventArgs e) => CoachMode.Open();

    /// <summary>Account, at the very foot: it opens beside the sidebar, and its icon shows it's open.</summary>
    void OnAccount(object sender, RoutedEventArgs e)
    {
        OpenBeside(typeof(AccountPage), new AccountOpening(sample, Root.RequestedTheme));
        ShowAccountChosen(true);
    }

    void ShowAccountChosen(bool chosen)
    {
        AccountIcon.Visibility = chosen ? Visibility.Collapsed : Visibility.Visible;
        AccountIconChosen.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The taf the reference set opens: the sample's Refunds, on its Activity (taf.png), its Setup (taf-setup.png) or its Card (taf-card.png).</summary>
    TafOpening SampleTaf(Tafs.Core.Words.WordBook w, TafTab opensOn) => SampleTaf(w, "refunds", opensOn);

    /// <summary>
    /// A taf's page from its line: from Home's answer, its head (its state in the app's words) and its Card bar; its
    /// Activity, Setup and Cards come with their reads. Otherwise the sample's.
    /// </summary>
    TafOpening OpenTaf(Tafs.Core.Words.WordBook w, string key) =>
        home?.Tafs.FirstOrDefault(t => t.Id == key) is { } taf
            ? new(TafScreen.Head(w, taf.Name, taf.Colour, taf.StateKey), taf.Cards.FirstOrDefault()?.Bar(w, taf.Cards.Count),
                filter => new ActivityPage([], [], filter, null, null))
            : SampleTaf(w, key, TafTab.Activity);

    /// <summary>
    /// A sample taf's page, opened from its Overview line: its head (Waiting when a call of its waits), its Card bar and its
    /// Activity. Refunds, the one the reference set shows, has its Setup and its Cards too.
    /// </summary>
    TafOpening SampleTaf(Tafs.Core.Words.WordBook w, string key, TafTab opensOn)
    {
        var now = DateTimeOffset.UtcNow;
        var taf = SampleTafs.All(w).First(t => t.Key == key);
        var head = TafScreen.Head(w, taf.Name, taf.Colour, held.Any(h => h.Taf == taf.Name) ? "waiting" : "working");
        var activity = (Func<string, ActivityPage>)(filter => TafActivity.Sample(w, taf.Name, filter, now, TimeZoneInfo.Local));
        return key == "refunds"
            ? new(head, TafScreen.SampleCard(w), activity, new SetupOpening(TafSetup.SampleJob(w), TafSetup.SampleNotes(w, now)), opensOn,
                TafScreen.SampleCards(w), w.Get("common.you"))
            : new(head, taf.Card.Bar(w, 1), activity);
    }

    /// <summary>A screen that isn't a sidebar row: no row stays chosen while it's open.</summary>
    void OpenBeside(Type page, object? parameter = null)
    {
        Nav.SelectedItem = null;
        ShowAccountChosen(false);
        Pages.Navigate(page, parameter);
    }

    void OnAddThisDevice(object sender, RoutedEventArgs e) => OpenPairing();

    /// <summary>Add this computer with your phone. It isn't a sidebar item: it opens from the notice, and from sign-in.</summary>
    /// For a screenshot, it stays offline: no TPM key, no Windows Hello, no claim.
    void OpenPairing() => OpenBeside(typeof(PairingPage), screenshot is not null);

    readonly string? screenshot;

    /// <summary>Sign in with KIFF: the first screen of a PC with no session.</summary>
    void OpenSignIn() => OpenBeside(typeof(SignInPage));

    /// <summary>Saves the window as a PNG and quits: the screenshots posted with each PR that changes a screen.</summary>
    async Task SaveAndQuitAsync(string file)
    {
        await Task.Delay(500); // let the first layout settle
        // Launched with no input, the first focus counts as the keyboard's and draws its ring, which a person clicking
        // never sees: the same element, focused as by the pointer, keeps the shot as the screen looks.
        if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot) is Control focused) focused.Focus(FocusState.Pointer);
        await Task.Delay(100);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(Root);
        var pixels = await bitmap.GetPixelsAsync();

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();

        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        await File.WriteAllBytesAsync(Path.GetFullPath(file), bytes);
        Application.Current.Exit();
    }
}

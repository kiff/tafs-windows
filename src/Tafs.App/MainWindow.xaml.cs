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
    readonly bool sample;
    readonly UsageReads usage;
    readonly ConnectAppsList? connectApps;

    /// <summary>The view the Calendar opens on: Week; Day or Month for `--page calendar` or `calendar-month`.</summary>
    CalendarMode calendarOpensOn = CalendarMode.Week;

    /// <param name="page">A screen to open first: "pair" for Add this computer, "sign-in", "needs-you", "record", "calendar" (Day), "calendar-week", "calendar-month", "usage", "connect-apps", "account", or "taf" (the sample's Refunds, with --sample). Otherwise the Overview.</param>
    /// <param name="viewOnly">The session can't approve yet: show the View only notice above every screen.</param>
    /// <param name="sample">Show the iPad's sample held calls and Record: for screenshots.</param>
    public MainWindow(string? screenshot, string? theme, string? page, bool viewOnly, bool sample = false)
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
        // No tafs list yet: it comes from tafs cloud in its own change.
        NoTafs.Content = Sidebar.NoTafs(w);

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
        else if (page == "connect-apps") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.ConnectApps);
        else if (page == "record") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.Record);
        else if (page == "needs-you") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.NeedsYou);
        else if (page == "taf" && sample) OpenBeside(typeof(TafPage), SampleTaf(w));
        else Nav.SelectedItem = BrandItem;
        if (screenshot is not null) Root.Loaded += async (_, _) => await SaveAndQuitAsync(screenshot);
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
        if (item == BrandItem) Pages.Navigate(typeof(OverviewPage), held);
        else if (item.Tag is Screen.NeedsYou or HeldCall) Pages.Navigate(typeof(NeedsYouPage), held);
        else if (item.Tag is Screen.Record) Pages.Navigate(typeof(RecordPage), record);
        else if (item.Tag is Screen.Usage) Pages.Navigate(typeof(UsagePage), usage);
        else if (item.Tag is Screen.ConnectApps) Pages.Navigate(typeof(ConnectAppsPage), connectApps);
        else if (item.Tag is Screen.Calendar) Pages.Navigate(typeof(CalendarPage), calendarOpensOn);
        else Pages.Navigate(typeof(ComingPage), item.Content);
    }

    /// <summary>Coach at the sidebar's foot. Coach itself comes in its own change, in the spec's order.</summary>
    void OnCoach(object sender, RoutedEventArgs e) => OpenBeside(typeof(ComingPage), CoachText.Text);

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

    /// <summary>A screen that isn't a sidebar row: no row stays chosen while it's open.</summary>
    /// <summary>The taf the reference set opens: the sample's Refunds, on its Activity.</summary>
    static TafOpening SampleTaf(Tafs.Core.Words.WordBook w)
    {
        var now = DateTimeOffset.UtcNow;
        return new(TafScreen.SampleHead(w), TafScreen.SampleCard(w),
            filter => TafActivity.Sample(w, TafScreen.SampleTaf(w), filter, now, TimeZoneInfo.Local));
    }

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

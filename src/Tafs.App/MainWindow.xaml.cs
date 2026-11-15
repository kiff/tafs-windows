using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Tafs.Core.Home;
using Tafs.Core.NeedsYou;
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

    /// <param name="page">A screen to open first: "pair" for Add this computer, "sign-in", "needs-you". Otherwise the Overview.</param>
    /// <param name="viewOnly">The session can't approve yet: show the View only notice above every screen.</param>
    /// <param name="sample">Show the iPad's sample held calls in Needs you: for screenshots.</param>
    public MainWindow(string? screenshot, string? theme, string? page, bool viewOnly, bool sample = false)
    {
        InitializeComponent();
        if (theme is "light" or "dark") Root.RequestedTheme = theme == "light" ? ElementTheme.Light : ElementTheme.Dark;
        var w = App.Words;
        Title = w.Get("app.name");
        AppWindow.Resize(LaptopSize);

        Brand.Text = w.Get("app.name");
        AutomationProperties.SetName(BrandItem, w.Get("home.overview.title"));
        held = sample ? NeedsYouScreen.Sample(w, DateTimeOffset.UtcNow) : [];
        var at = Nav.MenuItems.IndexOf(BrandItem) + 1;
        foreach (var item in Sidebar.Screens(w))
        {
            var row = new NavigationViewItem
            {
                Content = item.Title,
                Tag = item.Screen,
                Icon = new FontIcon { Glyph = item.Glyph },
                Style = (Style)Application.Current.Resources["TafsSidebarItem"],
            };
            if (item.Screen == Screen.NeedsYou) ShowCount(row, w, item.Title);
            Nav.MenuItems.Insert(at++, row);
        }
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
        ViewOnly.IsOpen = viewOnly;

        SignInPage.PairInsteadChosen += OpenPairing;
        NeedsYouPage.AddThisDeviceChosen += OpenPairing;
        SignInPage.SignedIn += _ =>
        {
            // Kept in memory for now. Until this PC is paired (or has its own passkey), it reads and doesn't approve.
            ViewOnly.IsOpen = true;
            Nav.SelectedItem = BrandItem;
        };

        if (page == "pair") OpenPairing();
        else if (page == "sign-in") OpenSignIn();
        else if (page == "needs-you") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.NeedsYou);
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

    void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // The open screen's row is heavier, with no fill, as on the owner's iPad; the others regular.
        foreach (var row in Nav.MenuItems.OfType<NavigationViewItem>().Where(r => r != BrandItem))
            row.FontWeight = ReferenceEquals(row, args.SelectedItem) ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        if (args.SelectedItem is not NavigationViewItem item) return;
        ShowAccountChosen(false);
        if (item == BrandItem) Pages.Navigate(typeof(OverviewPage));
        else if (item.Tag is Screen.NeedsYou) Pages.Navigate(typeof(NeedsYouPage), held);
        else Pages.Navigate(typeof(ComingPage), item.Content);
    }

    /// <summary>Coach at the sidebar's foot. Coach itself comes in its own change, in the spec's order.</summary>
    void OnCoach(object sender, RoutedEventArgs e) => OpenBeside(typeof(ComingPage), CoachText.Text);

    /// <summary>Account, at the very foot: it opens beside the sidebar, and its icon shows it's open.</summary>
    void OnAccount(object sender, RoutedEventArgs e)
    {
        OpenBeside(typeof(ComingPage), AccountTitle.Text);
        ShowAccountChosen(true);
    }

    void ShowAccountChosen(bool chosen)
    {
        AccountIcon.Visibility = chosen ? Visibility.Collapsed : Visibility.Visible;
        AccountIconChosen.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
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
    void OpenPairing() => OpenBeside(typeof(PairingPage));

    /// <summary>Sign in with KIFF: the first screen of a PC with no session.</summary>
    void OpenSignIn() => OpenBeside(typeof(SignInPage));

    /// <summary>Saves the window as a PNG and quits: the screenshots posted with each PR that changes a screen.</summary>
    async Task SaveAndQuitAsync(string file)
    {
        await Task.Delay(500); // let the first layout settle
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

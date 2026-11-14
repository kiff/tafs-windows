using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace Tafs.App;

public sealed partial class MainWindow : Window
{
    /// <summary>A laptop's window: the size the PR screenshots are taken at.</summary>
    static readonly SizeInt32 LaptopSize = new(1280, 800);

    public MainWindow(string? screenshot, string? theme)
    {
        InitializeComponent();
        if (theme is "light" or "dark") Root.RequestedTheme = theme == "light" ? ElementTheme.Light : ElementTheme.Dark;
        var w = App.Words;
        Title = w.Get("app.name");
        AppWindow.Resize(LaptopSize);

        Brand.Text = w.Get("app.name");
        OverviewItem.Content = w.Get("home.overview.title");
        NeedsYouItem.Content = w.Get("needs_you.title");
        RecordItem.Content = w.Get("record.title");
        CalendarItem.Content = w.Get("calendar.heading");
        UsageItem.Content = w.Get("usage.title");
        ConnectItem.Content = w.Get("connect.apps.title");
        YourTafsHeader.Content = w.Get("home.your_tafs");
        NoTafsItem.Content = w.Get("home.no_tafs");
        CoachItem.Content = w.Get("coach.title");
        AccountItem.Content = w.Get("settings.title");

        Nav.SelectedItem = OverviewItem;
        if (screenshot is not null) Root.Loaded += async (_, _) => await SaveAndQuitAsync(screenshot);
    }

    void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var item = (NavigationViewItem)args.SelectedItem;
        if ((string)item.Tag == "overview") Pages.Navigate(typeof(OverviewPage));
        else Pages.Navigate(typeof(ComingPage), item.Content);
    }

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

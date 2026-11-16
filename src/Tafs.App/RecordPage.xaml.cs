using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Tafs.Core.NeedsYou;
using Tafs.Core.Record;

namespace Tafs.App;

/// <summary>One day of the Record, as drawn.</summary>
public sealed record RecordDayView(string Title, IReadOnlyList<RecordLineView> Entries);

/// <summary>
/// One line of the Record, as drawn: its status dot, its taf's colour, "Taf · what", the time and the detail; read out as
/// one, with what a tap does.
/// </summary>
public sealed record RecordLineView(string Title, string Time, string Detail, Brush TafColour, Visibility IsDone, Visibility IsWaiting, Visibility IsRefused,
    RecordEntry Entry)
{
    public string Name => $"{Title}, {Time}, {Detail}";
    public string Hint => App.Words.Get("record.shows_receipt");

    public static RecordLineView Of(RecordEntry e, TimeZoneInfo zone)
    {
        var (title, time) = RecordScreen.Line(e, zone);
        static Visibility Seen(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
        var dot = RecordScreen.DotOf(e.Kind);
        return new(title, time, e.Detail, Colour(e.TafColor), Seen(dot == Dot.Went), Seen(dot == Dot.Waiting), Seen(dot == Dot.Stopped), e);
    }

    public static Brush Colour(int taf)
    {
        var rgb = TafColors.Of(taf);
        return new SolidColorBrush(ColorHelper.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }
}

/// <summary>The Record: KIFF's activity for the session's tafs, or the iPad's sample; redrawn as each read lands.</summary>
public sealed partial class RecordPage : Page
{
    IReadOnlyList<RecordEntry> entries = [];
    RecordFilter filter = RecordFilter.All;
    string? taf;

    public RecordPage()
    {
        InitializeComponent();
        // Export receipts is drawn in the page's own theme: again once it's in the window, and whenever the theme changes.
        Loaded += (_, _) => ShowExport();
        ActualThemeChanged += (_, _) => ShowExport();
    }

    /// <summary>tafs cloud's answer as it came, as the session (an export's bytes); none signed out, and no Export receipts.</summary>
    public static Func<string, Task<(byte[]? Raw, string? Why)>>? ReadBytes { get; set; }

    /// <summary>Whether this person sees every taf's Record, which the export needs, as the window reads it; tafs cloud still decides (403).</summary>
    public static bool SeesEveryTaf { get; set; }

    bool exportOpen;
    bool exporting;
    string? exportWhy;
    int exportMissing;

    /// <summary>
    /// Export receipts, as the iPad's <c>ExportReceiptsRow</c>: the row opens the ranges (the last 7, 30 or 90 days, today
    /// among them); one exports them, "Preparing receipts…" meanwhile, then shares the two files through Windows' share
    /// sheet, with how many calls have no receipt yet; not exported, why, in red.
    /// </summary>
    void ShowExport()
    {
        var w = App.Words;
        Export.Children.Clear();
        Export.Visibility = SeesEveryTaf && ReadBytes is not null ? Visibility.Visible : Visibility.Collapsed;
        if (Export.Visibility != Visibility.Visible) return;
        var row = new HyperlinkButton
        {
            Content = w.Get(exporting ? "record.export_preparing" : "record.export_receipts"), IsEnabled = !exporting, Padding = new Thickness(0),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ChatViews.ThemeBrush(ActualTheme, exporting ? "TafsDim" : "TafsAmberText"),
        };
        AutomationProperties.SetAutomationId(row, "export-receipts");
        row.Click += (_, _) => { exportOpen = !exportOpen; ShowExport(); };
        Export.Children.Add(row);
        if (exportOpen && !exporting)
        {
            var days = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var count in ReceiptsExport.Days)
            {
                var b = new Button { Content = w.Count("record.export_last_days", count), Style = (Style)Application.Current.Resources["TafsOutlineButton"] };
                b.Click += (_, _) => _ = ExportAsync(count);
                days.Children.Add(b);
            }
            Export.Children.Add(days);
        }
        if (exportMissing > 0)
            Export.Children.Add(new TextBlock { Text = w.Count("record.export_missing", exportMissing), FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Foreground = ChatViews.ThemeBrush(ActualTheme, "TafsSoft") });
        if (exportWhy is { } why)
            Export.Children.Add(new TextBlock { Text = why, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = ChatViews.ThemeBrush(ActualTheme, "TafsNo") });
    }

    async Task ExportAsync(int count)
    {
        if (exporting || ReadBytes is not { } read) return;
        var w = App.Words;
        var (from, to) = ReceiptsExport.Range(count, DateOnly.FromDateTime(DateTime.Now));
        (exporting, exportWhy, exportMissing) = (true, null, 0);
        ShowExport();
        var (raw, why) = await read(ReceiptsExport.Path(from, to));
        exporting = false;
        if (raw is not null && ReceiptsExport.Read(raw, from, to) is { } export)
        {
            try
            {
                var files = ReceiptsExport.Write(ReceiptsExport.Folder, export);
                (exportMissing, exportOpen) = (export.Missing, false);
                ShowExport();
                ShareFiles.Show(files, w.Get("record.export_receipts"));
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { why = e.Message; }
        }
        exportWhy = w.Get("record.export_not_exported", ("why", why ?? w.Get("error.ours")));
        ShowExport();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        (Heading.Text, Lead.Text) = RecordScreen.Heading(w);
        ShowEntries(e.Parameter as IReadOnlyList<RecordEntry> ?? []);
        ShowExport();
    }

    /// <summary>New lines, as a read lands: the chosen taf and filter stay, as the iPad's do across a refresh.</summary>
    public void ShowEntries(IReadOnlyList<RecordEntry> lines)
    {
        entries = lines;
        Show();
    }

    /// <summary>A line's sheet: its facts, what happened and KIFF's receipt.</summary>
    async void OnLine(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecordEntry entry }) await ReceiptSheet.ShowAsync(XamlRoot, ActualTheme, entry);
    }

    /// <summary>Draws the chips, the chosen ones inverted, and the days the filter and the taf leave.</summary>
    void Show()
    {
        var w = App.Words;
        TafChips.Children.Clear();
        TafChips.Children.Add(Chip(RecordScreen.AllTafs(w), null, taf is null, () => taf = null));
        foreach (var (name, colour) in RecordScreen.TafChips(entries))
            TafChips.Children.Add(Chip(name, RecordLineView.Colour(colour), taf == name, () => taf = name));

        FilterChips.Children.Clear();
        foreach (var (f, title) in RecordScreen.Filters(w))
            FilterChips.Children.Add(Chip(title, null, filter == f, () => filter = f));

        var days = RecordScreen.Days(w, entries, filter, taf, DateTimeOffset.Now, TimeZoneInfo.Local);
        Days.ItemsSource = days.Select(d => new RecordDayView(d.Title, d.Entries.Select(x => RecordLineView.Of(x, TimeZoneInfo.Local)).ToList())).ToList();
        Empty.Text = RecordScreen.Empty(w, filter, taf);
        Empty.Visibility = days.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A chip, as the iPad's: 13, in a capsule; the chosen one inverts, the ink's fill with the ground's colour on it.</summary>
    Button Chip(string title, Brush? dot, bool chosen, Action choose)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (dot is not null) content.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = dot, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = title, FontSize = 13 });
        var chip = new Button
        {
            Content = content,
            Style = (Style)Application.Current.Resources[chosen ? "TafsChipChosen" : "TafsChip"],
        };
        AutomationProperties.SetName(chip, title);
        if (chosen) AutomationProperties.SetItemStatus(chip, "selected");
        chip.Click += (_, _) => { choose(); Show(); };
        return chip;
    }
}

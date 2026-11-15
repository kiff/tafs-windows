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

/// <summary>One line of the Record, as drawn: its status dot, its taf's colour, "Taf · what", the time and the detail.</summary>
public sealed record RecordLineView(string Title, string Time, string Detail, Brush TafColour, Visibility IsDone, Visibility IsWaiting, Visibility IsRefused)
{
    public static RecordLineView Of(RecordEntry e, TimeZoneInfo zone)
    {
        var (title, time) = RecordScreen.Line(e, zone);
        static Visibility Seen(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
        return new(title, time, e.Detail, Colour(e.TafColor),
            Seen(e.Kind is EntryKind.Done or EntryKind.Started), Seen(e.Kind == EntryKind.Held), Seen(e.Kind == EntryKind.Refused));
    }

    public static Brush Colour(int taf)
    {
        var rgb = TafColors.Of(taf);
        return new SolidColorBrush(ColorHelper.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }
}

/// <summary>The Record. Its lines come from tafs cloud and KIFF in their own change: until then none, or the iPad's sample.</summary>
public sealed partial class RecordPage : Page
{
    IReadOnlyList<RecordEntry> entries = [];
    RecordFilter filter = RecordFilter.All;
    string? taf;

    public RecordPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        (Heading.Text, Lead.Text) = RecordScreen.Heading(w);
        entries = e.Parameter as IReadOnlyList<RecordEntry> ?? [];
        Show();
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

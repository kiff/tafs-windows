using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Shapes;
using Tafs.Core.Home;
using Tafs.Core.NeedsYou;

namespace Tafs.App;

/// <summary>A held call on the Overview, under NEEDS YOU.</summary>
public sealed record HeldLine(string Title, string AnswerBy)
{
    public string Name => $"{Title}, {AnswerBy}";
}

/// <summary>What the Overview opens with: the held calls, and the tafs' lines (none until tafs cloud lists them, or the sample's).</summary>
public sealed record OverviewOpening(IReadOnlyList<HeldCall> Held, IReadOnlyList<OverviewRow> Rows);

public sealed partial class OverviewPage : Page
{
    /// <summary>A taf's line was chosen: its page opens beside the sidebar.</summary>
    public static event Action<string>? TafChosen;

    /// <summary>A held call's row was chosen: Needs you opens.</summary>
    public static event Action? NeedsYouChosen;

    void OnHeld(object sender, RoutedEventArgs e) => NeedsYouChosen?.Invoke();

    public OverviewPage()
    {
        InitializeComponent();
        var lines = OverviewScreen.Lines(App.Words);
        Heading.Text = lines.Title;
        Subtitle.Text = lines.Subtitle;
        ColumnTaf.Text = lines.Columns[0];
        ColumnNow.Text = lines.Columns[1];
        ColumnCard.Text = lines.Columns[2];
        ColumnLast.Text = lines.Columns[3];
        Empty.Text = lines.Empty;
        Footnote.Text = lines.Footnote;
    }

    /// <summary>The held calls, when there are some: NEEDS YOU above the tafs, each with when to answer by.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        var opening = e.Parameter as OverviewOpening;
        NeedsYouLabel.Text = Sidebar.Eyebrow(w, "needs_you.title");
        ShowHeld(opening?.Held ?? []);
        ShowRows(opening?.Rows ?? []);
    }

    /// <summary>NEEDS YOU's lines, drawn again with each read of KIFF's held calls; none, no section.</summary>
    internal void ShowHeld(IReadOnlyList<HeldCall> held)
    {
        var w = App.Words;
        Held.ItemsSource = held.Select(c => NeedsYouScreen.OverviewRow(w, c, TimeZoneInfo.Local))
            .Select(r => new HeldLine(r.Title, r.AnswerBy)).ToList();
        NeedsYou.Visibility = held.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// One line per taf under the headings, a line between them, as the iPad's grid: its avatar and name, what it's doing
    /// now, its Card, and its last result in the soft colour. The whole line opens the taf, and Narrator reads it as one.
    /// </summary>
    internal void ShowRows(IReadOnlyList<OverviewRow> rows)
    {
        Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // Drawn again when tafs cloud answers: the lines under the headings go first.
        foreach (var old in Table.Children.OfType<FrameworkElement>().Where(c => Grid.GetRow(c) >= 2).ToList()) Table.Children.Remove(old);
        while (Table.RowDefinitions.Count > 2) Table.RowDefinitions.RemoveAt(2);
        var w = App.Words;
        foreach (var row in rows)
        {
            var at = Table.RowDefinitions.Count;
            Table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = new Rectangle { Height = 1, Style = (Style)Application.Current.Resources["TafsLineRule"] };
            Grid.SetRow(line, at);
            Grid.SetColumnSpan(line, 4);
            Table.Children.Add(line);

            var cells = new Grid { ColumnSpacing = 16 };
            for (var i = 0; i < 4; i++) cells.ColumnDefinitions.Add(new ColumnDefinition());
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            name.Children.Add(new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = RecordLineView.Colour(row.Colour),
                Child = new TextBlock { Text = Tafs.Core.Taf.TafScreen.Initial(row.Taf), FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            name.Children.Add(new TextBlock { Text = row.Taf, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Style = Look("TafsInkText") });
            cells.Children.Add(name);
            foreach (var (text, column, style) in new[] { (row.Now, 1, "TafsInkText"), (row.Card, 2, "TafsInkText"), (row.LastResult, 3, "TafsSoftText") })
            {
                var cell = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxLines = column == 3 ? 2 : 0, Style = Look(style) };
                Grid.SetColumn(cell, column);
                cells.Children.Add(cell);
            }
            var open = new Button { Style = Look("TafsBox"), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Content = cells };
            AutomationProperties.SetName(open, string.Join(", ", row.Taf, row.Now, row.Card, row.LastResult));
            AutomationProperties.SetHelpText(open, w.Get("people.opens_taf"));
            open.Click += (_, _) => TafChosen?.Invoke(row.Key);
            Grid.SetRow(open, at + 1);
            Grid.SetColumnSpan(open, 4);
            Table.Children.Add(open);
        }
    }

    static Style Look(string key) => (Style)Application.Current.Resources[key];
}

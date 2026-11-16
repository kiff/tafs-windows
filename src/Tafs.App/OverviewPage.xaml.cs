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

/// <summary>
/// What the Overview opens with: the held calls, the tafs' lines (none until tafs cloud lists them, or the sample's), and
/// the account's labels.
/// </summary>
public sealed record OverviewOpening(IReadOnlyList<HeldCall> Held, IReadOnlyList<OverviewRow> Rows, HomeLabels? Labels = null,
    GalleryOpening? Recipes = null);

public sealed partial class OverviewPage : Page
{
    /// <summary>A taf's line was chosen: its page opens beside the sidebar.</summary>
    public static event Action<string>? TafChosen;

    /// <summary>A held call's row was chosen: Needs you opens.</summary>
    public static event Action? NeedsYouChosen;

    void OnHeld(object sender, RoutedEventArgs e) => NeedsYouChosen?.Invoke();

    /// <summary>Every taf's line, before a chip filters them.</summary>
    IReadOnlyList<OverviewRow> allRows = [];
    HomeLabels labels = HomeLabels.None;
    /// <summary>The chosen label: none is All. The list opens on All every time, as the iPad's.</summary>
    string? chosen;
    /// <summary>"+N" was clicked: every label shows.</summary>
    bool expanded;

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
        ShowRows(opening?.Rows ?? [], opening?.Labels);
        ShowRecipes(opening?.Rows.Count == 0 ? opening.Recipes : null);
    }

    /// <summary>
    /// With no taf, the recipes tafs cloud lists, in place of the empty table and its footnote, as the iPad's HomeRecipes; none,
    /// the table as before.
    /// </summary>
    internal void ShowRecipes(GalleryOpening? recipes)
    {
        var shown = recipes is not null;
        Table.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        Footnote.Visibility = shown ? Visibility.Collapsed : Visibility.Visible;
        Recipes.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (recipes is not null) Recipes.Navigate(typeof(GalleryPage), recipes);
        else Recipes.Content = null;
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
    internal void ShowRows(IReadOnlyList<OverviewRow> rows, HomeLabels? accountLabels = null)
    {
        allRows = rows;
        labels = accountLabels ?? HomeLabels.None;
        // A label that's gone goes back to All.
        chosen = LabelChips.Chosen(chosen, labels);
        ShowChips();
        DrawRows(LabelChips.Filtered(allRows, r => r.LabelIds, chosen, labels));
    }

    /// <summary>
    /// The labels' chips, as the iPad's <c>LabelChips</c>: All, then two labels with their count and an amber dot while one
    /// of their tafs waits, then "+N" for the rest. The chosen one is filled; clicked again, it goes back to All.
    /// </summary>
    void ShowChips()
    {
        Chips.Children.Clear();
        LabelRow.Visibility = labels.Labels.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (labels.Labels.Count == 0) return;
        Chips.Children.Add(Chip(LabelChips.All(App.Words, labels), null, false, chosen is null, () => Choose(null)));
        var (shown, hidden) = LabelChips.Row(labels, expanded);
        foreach (var label in shown)
            Chips.Children.Add(Chip(label.Name, label.Count, label.Waiting > 0, chosen == label.Id,
                () => Choose(chosen == label.Id ? null : label.Id), "label." + label.Id));
        if (hidden > 0) Chips.Children.Add(Chip($"+{hidden}", null, false, false, () => { expanded = true; ShowChips(); }));
    }

    void Choose(string? label)
    {
        chosen = label;
        ShowChips();
        DrawRows(LabelChips.Filtered(allRows, r => r.LabelIds, chosen, labels));
    }

    static Button Chip(string words, int? count, bool waiting, bool selected, Action choose, string? id = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        if (waiting) content.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TafsAmber"], VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = words, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center });
        if (count is { } n) content.Children.Add(new TextBlock { Text = n.ToString(System.Globalization.CultureInfo.CurrentCulture), FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
        var chip = new Button { Content = content, MinHeight = 32, Style = Look(selected ? "TafsChipChosen" : "TafsChip") };
        AutomationProperties.SetName(chip, count is { } c ? $"{words}, {c}" : words);
        if (id is not null) AutomationProperties.SetAutomationId(chip, id);
        chip.Click += (_, _) => choose();
        return chip;
    }

    /// <summary>The lines under the headings, for the tafs the chosen chip shows.</summary>
    void DrawRows(IReadOnlyList<OverviewRow> rows)
    {
        Empty.Visibility = allRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // The drawings, once, in this page's theme: board 10 always (a desktop is the iPad's regular width), board 2 with no
        // taf (the recipes, when tafs cloud lists them, take the table's place instead).
        OverviewArt.Child ??= Illustrations.Overview();
        NoTafsArt.Visibility = Empty.Visibility;
        if (allRows.Count == 0) NoTafsArt.Child ??= Illustrations.NoTafs();
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
            var title = new TextBlock { Text = row.Taf, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Style = Look("TafsInkText") };
            if (row.Chips is { Count: > 0 } own)
            {
                // Its labels under its name, as the iPad's TafLabels: small grey chips, two then "+1".
                var under = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                foreach (var words in own)
                    under.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 2, 7, 2),
                        Background = ThemeBrush("TafsRaised"),
                        Child = new TextBlock { Text = words, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Style = Look("TafsSoftText") },
                    });
                AutomationProperties.SetAutomationId(under, "taf.labels");
                name.Children.Add(new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { title, under } });
            }
            else name.Children.Add(title);
            cells.Children.Add(name);
            foreach (var (text, column, style) in new[] { (row.Now, 1, "TafsInkText"), (row.Card, 2, "TafsInkText"), (row.LastResult, 3, "TafsSoftText") })
            {
                var cell = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxLines = column == 3 ? 2 : 0, Style = Look(style) };
                Grid.SetColumn(cell, column);
                cells.Children.Add(cell);
            }
            var open = new Button { Style = Look("TafsBox"), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Content = cells };
            AutomationProperties.SetName(open, string.Join(", ", new[] { row.Taf }.Concat(row.Chips ?? []).Concat([row.Now, row.Card, row.LastResult])));
            AutomationProperties.SetHelpText(open, w.Get("people.opens_taf"));
            open.Click += (_, _) => TafChosen?.Invoke(row.Key);
            Grid.SetRow(open, at + 1);
            Grid.SetColumnSpan(open, 4);
            Table.Children.Add(open);
        }
    }

    static Style Look(string key) => (Style)Application.Current.Resources[key];

    /// <summary>A theme brush in this page's theme, as NeedsYouPage finds one: Theme.xaml's dictionaries first.</summary>
    Microsoft.UI.Xaml.Media.Brush ThemeBrush(string key)
    {
        var theme = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var merged in Application.Current.Resources.MergedDictionaries.Prepend(Application.Current.Resources))
            if (merged.ThemeDictionaries.TryGetValue(theme, out var found) && found is ResourceDictionary d && d.TryGetValue(key, out var brush)
                && brush is Microsoft.UI.Xaml.Media.Brush b)
                return b;
        return (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[key];
    }
}

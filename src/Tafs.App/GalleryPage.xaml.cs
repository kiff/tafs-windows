using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Home;

namespace Tafs.App;

/// <summary>What the gallery opens with.</summary>
/// <param name="Browse">Browsing (Explore jobs, See all jobs): nothing is created until "Try it with Coach" is clicked on one.</param>
/// <param name="Hand">A job's one button, for someone who may use Coach: Coach opens with the job's words. None, no button.</param>
/// <param name="Open">A job to open on its sheet at once (one from the sidebar's strip).</param>
/// <param name="Logos">Where a logo's path loads from: tafs cloud only. None, the app's name alone.</param>
/// <param name="OwnWords">From Start a new taf, for someone who may use Coach: "Say it in your own words", Coach with an empty box.</param>
public sealed record GalleryOpening(Gallery Gallery, bool Browse, Action<Recipe>? Hand, string? Open = null, Uri? Logos = null,
    Action? OwnWords = null);

/// <summary>
/// The jobs' gallery, as the iPad's <c>RecipeGallerySheet</c> with <c>HomeRecipes</c>, <c>RecipeRow</c> and <c>RecipeSheet</c>:
/// every word tafs cloud's, and a job's sheet with its Card in plain zones and one button that hands it to Coach.
/// </summary>
public sealed partial class GalleryPage : Page
{
    GalleryOpening? opening;
    /// <summary>The category picked to narrow the list; picked again, every job shows.</summary>
    string? picked;
    bool opened;

    public GalleryPage()
    {
        InitializeComponent();
        // Its colours are the window's theme's, known once it's in the window: drawn again then, and when the theme changes.
        Loaded += (_, _) => Show();
        ActualThemeChanged += (_, _) => Show();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        opening = e.Parameter as GalleryOpening;
        Show();
    }

    void Show()
    {
        if (opening is not { Gallery: var g } o) return;
        Heading.Text = g.Title(o.Browse);
        Body.Children.Clear();
        // Creating, as the iPad's + New: "Say it in your own words" leads, in tafs cloud's words, where it sends them.
        if (!o.Browse && o.OwnWords is { } own && g.Words.OwnWords.Trim().Length > 0) Body.Children.Add(OwnWordsRow(g.Words, own));
        if (g.Words.All.Trim().Length > 0) Body.Children.Add(Eyebrow(g.Words.All));
        var all = g.ByCategory;
        if (all.Count > 1)
        {
            var chips = new WrapRow { Spacing = 8 };
            foreach (var (category, _) in all) chips.Children.Add(Chip(category));
            Body.Children.Add(chips);
        }
        foreach (var (category, recipes) in g.Groups(picked))
        {
            Body.Children.Add(Eyebrow(category.Title));
            foreach (var r in recipes) Body.Children.Add(Row(r, category));
        }
        if (!opened && o.Open is { } id && g.Recipes.FirstOrDefault(r => r.Id == id) is { } job && XamlRoot is not null)
        {
            opened = true;
            _ = OpenAsync(job);
        }
    }

    /// <summary>A category's chip, in its colour where tafs cloud sent one; picked, filled with it.</summary>
    Button Chip(RecipeCategory category)
    {
        var on = picked == category.Id;
        var tint = Tint(category);
        var chip = new Button
        {
            Content = category.Title, FontSize = 13, MinHeight = 32, Padding = new Thickness(12, 4, 12, 4), CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Background = on ? tint ?? Look("TafsInk") : Look("TafsPanel"),
            Foreground = on ? Look("TafsOnFill") : tint ?? Look("TafsSoft"),
            BorderBrush = on ? tint ?? Look("TafsInk") : tint ?? Look("TafsLine"),
        };
        AutomationProperties.SetName(chip, category.Title);
        if (on) AutomationProperties.SetItemStatus(chip, "selected");
        chip.Click += (_, _) =>
        {
            picked = on ? null : category.Id;
            Show();
        };
        return chip;
    }

    /// <summary>One job as a card: the problem it ends, its line, and its apps, on an edge in its category's colour.</summary>
    Button Row(Recipe r, RecipeCategory? category)
    {
        var lines = new StackPanel { Spacing = 6 };
        lines.Children.Add(Words(r.Lead, 16, "TafsInk", semibold: true));
        if (r.Line.Length > 0) lines.Children.Add(Words(r.Line, 14, "TafsSoft"));
        if (r.Apps.Count > 0) lines.Children.Add(Apps(r.Apps));
        var card = new Grid();
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (Tint(category) is { } tint) card.Children.Add(new Border { Background = tint });
        var inner = new Border { Padding = new Thickness(14, 12, 16, 12), Child = lines };
        Grid.SetColumn(inner, 1);
        card.Children.Add(inner);
        var row = new Button
        {
            Content = card, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1),
            Background = Look("TafsPanel"), BorderBrush = Look("TafsLine"),
        };
        AutomationProperties.SetName(row, r.Line.Length > 0 ? $"{r.Lead}. {r.Line}" : r.Lead);
        row.Click += (_, _) => _ = OpenAsync(r);
        return row;
    }

    /// <summary>"Say it in your own words" and its line, as the iPad's <c>OwnWordsRow</c>: Coach, with nothing in its box.</summary>
    Button OwnWordsRow(GalleryWords words, Action own)
    {
        var lines = new StackPanel { Spacing = 4 };
        lines.Children.Add(Words(words.OwnWords, 16, "TafsInk", semibold: true));
        if (words.OwnWordsLine.Trim().Length > 0) lines.Children.Add(Words(words.OwnWordsLine, 14, "TafsSoft"));
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(lines);
        var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 13, Foreground = Look("TafsDim"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 1);
        row.Children.Add(chevron);
        var b = new Button
        {
            Content = row, Padding = new Thickness(16, 14, 16, 14), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1),
            Background = Look("TafsPanel"), BorderBrush = Look("TafsLine"),
        };
        AutomationProperties.SetName(b, words.OwnWords);
        AutomationProperties.SetAutomationId(b, "gallery.own-words");
        b.Click += (_, _) => own();
        return b;
    }

    /// <summary>The apps a job uses, by name, each after its logo where tafs cloud serves one.</summary>
    WrapRow Apps(IReadOnlyList<RecipeApp> apps)
    {
        var row = new WrapRow { Spacing = 8 };
        foreach (var app in apps)
        {
            var mark = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            // Only tafs cloud's logos, from tafs cloud: anything else, the name alone. One that doesn't load shows nothing.
            if (opening?.Logos is { } logos && Gallery.IsLogo(app.Logo))
                mark.Children.Add(new Image { Width = 18, Height = 18, Source = new BitmapImage(new Uri(logos, app.Logo)) });
            mark.Children.Add(Words(app.Name, 12, "TafsDim"));
            row.Children.Add(mark);
        }
        return row;
    }

    /// <summary>
    /// A job's sheet, as the iPad's <c>RecipeSheet</c>: what it ends and what that costs today, its Card in zones, tafs
    /// cloud's line for a passed proof run and what it reported, then one button that hands it to Coach.
    /// </summary>
    async Task OpenAsync(Recipe r)
    {
        if (opening is not { Gallery: var g } o) return;
        var lines = new StackPanel { Spacing = 12 };
        if (r.Line.Length > 0) lines.Children.Add(Words(r.Line, 15, "TafsInk"));
        if (r.Today.Length > 0) lines.Children.Add(Words(r.Today, 14, "TafsSoft"));
        foreach (var zone in g.Zones(r))
        {
            var panel = new StackPanel { Spacing = 6 };
            if (zone.Title.Length > 0) panel.Children.Add(Eyebrow(zone.Title));
            foreach (var line in zone.Lines) panel.Children.Add(Words(line, 14, "TafsInk"));
            lines.Children.Add(Panel(panel));
        }
        if (r.Proved.Length > 0)
        {
            var proved = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            proved.Children.Add(new FontIcon { Glyph = "", FontSize = 14, Foreground = Look("TafsSoft") });
            proved.Children.Add(Words(r.Proved, 14, "TafsInk", semibold: true));
            AutomationProperties.SetAutomationId(proved, "recipe.proved");
            lines.Children.Add(proved);
        }
        if (r.Sample.Length > 0)
        {
            var sample = new StackPanel { Spacing = 6 };
            if (g.SampleLabel is { } label) sample.Children.Add(Eyebrow(label));
            sample.Children.Add(Words(r.Sample, 14, "TafsInk"));
            lines.Children.Add(Panel(sample));
        }
        var hand = o.Hand is not null ? g.HandWords(o.Browse) : null;
        var sheet = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = r.Lead, Content = new ScrollViewer { Content = lines, MaxHeight = 520 },
            CloseButtonText = App.Words.Get("common.close"),
            PrimaryButtonText = hand ?? "", DefaultButton = hand is null ? ContentDialogButton.Close : ContentDialogButton.Primary,
            RequestedTheme = ActualTheme,
        };
        AutomationProperties.SetAutomationId(sheet, "recipe.sheet");
        if (await sheet.ShowAsync() == ContentDialogResult.Primary && o.Hand is { } handIt) handIt(r);
    }

    Border Panel(UIElement child) => new()
    {
        Child = child, Padding = new Thickness(14, 12, 14, 12), CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
        Background = Look("TafsPanel"), BorderBrush = Look("TafsLine"),
    };

    TextBlock Eyebrow(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["TafsEyebrow"] };

    TextBlock Words(string text, double size, string brush, bool semibold = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Look(brush),
        FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
    };

    /// <summary>A category's colour, tafs cloud's token, light or dark as the window is; none when it sent none.</summary>
    Brush? Tint(RecipeCategory? category) =>
        category?.Color is { } t ? new SolidColorBrush(Hex(ActualTheme == ElementTheme.Dark ? t.Dark : t.Light)) : null;

    static Windows.UI.Color Hex(uint rgb) => Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    Brush Look(string key) => ChatViews.ThemeBrush(ActualTheme, key);
}

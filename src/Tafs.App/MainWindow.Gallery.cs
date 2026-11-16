using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Home;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// The jobs' gallery in the sidebar, as the iPad's <c>HomeSplitView</c>: under Your tafs, tafs cloud's "Explore jobs" row
/// (with or without tafs, and for anyone, since browsing creates nothing), then, with tafs, the strip of a few jobs the
/// account doesn't run yet under its title, and "See all jobs". Read with Home's list from <c>GET /v1/recipes/gallery</c>.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The gallery as last read; none, no rows.</summary>
    Gallery? gallery;

    bool galleryFollowsTheme;

    /// <summary>The gallery asked for on the command line, opened once it's read.</summary>
    string? galleryAsked;

    /// <summary>A gallery row: Explore jobs and See all jobs open it to browse; a strip job opens it on that job's sheet.</summary>
    sealed record GalleryRow(string? Job);

    /// <summary>The gallery, with each read of Home's list; one that lands after another session began is dropped.</summary>
    async Task ReadGalleryAsync(Func<string, Task<CloudRead>> read, int reading)
    {
        var answer = (await read(Gallery.Path)).Answer;
        if (reading != sessions) return;
        // A read that fails keeps what was shown.
        if (answer is { } json && Gallery.Read(json) is { } read2) gallery = read2;
        ShowGalleryRows();
        // For a screenshot (--page gallery, gallery-job): opened once it's read, on its first strip job's sheet for the second.
        if (galleryAsked is { } asked && gallery is { } g)
        {
            galleryAsked = null;
            OpenGallery(new GalleryRow(asked == "gallery-job" ? g.Recipes.FirstOrDefault()?.Id : null));
        }
    }

    /// <summary>The gallery's rows, drawn again after Your tafs: none without a gallery or tafs cloud's words.</summary>
    void ShowGalleryRows()
    {
        // The strip's colours are the window's theme's: drawn again when it changes.
        if (!galleryFollowsTheme)
        {
            galleryFollowsTheme = true;
            Root.ActualThemeChanged += (_, _) => ShowGalleryRows();
        }
        foreach (var old in Nav.MenuItems.OfType<FrameworkElement>().Where(i => i.Tag is GalleryRow).ToList()) Nav.MenuItems.Remove(old);
        if (gallery is not { } g) return;
        if (g.Explore is { } explore)
        {
            var row = Item(explore, new GalleryRow(null), "");
            AutomationProperties.SetAutomationId(row, "home.explore");
            Nav.MenuItems.Add(row);
        }
        // A few jobs the account doesn't run yet, where tafs cloud shows them, once there are tafs.
        if (home is not { Tafs.Count: > 0 } || g.StripRecipes.Count == 0) return;
        if (g.StripTitle.Trim().Length > 0)
            Nav.MenuItems.Add(new NavigationViewItemHeader
            {
                Content = g.StripTitle, Tag = new GalleryRow(null), IsHitTestVisible = false,
                Style = (Style)Application.Current.Resources["TafsSidebarHeader"],
            });
        foreach (var job in g.StripRecipes)
        {
            var line = new Grid { ColumnSpacing = 10 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.Children.Add(new Border
            {
                Height = 28, CornerRadius = new CornerRadius(2),
                Background = g.CategoryOf(job)?.Color is { } t
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)((Root.ActualTheme == ElementTheme.Dark ? t.Dark : t.Light) >> 16),
                        (byte)((Root.ActualTheme == ElementTheme.Dark ? t.Dark : t.Light) >> 8), (byte)(Root.ActualTheme == ElementTheme.Dark ? t.Dark : t.Light)))
                    : ChatViews.ThemeBrush(Root.ActualTheme, "TafsLine"),
            });
            var words = new TextBlock { Text = job.Lead, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxLines = 2, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(words, 1);
            line.Children.Add(words);
            var row = new NavigationViewItem
            {
                Content = line, Tag = new GalleryRow(job.Id), Style = (Style)Application.Current.Resources["TafsSidebarItem"],
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(row, job.Lead);
            Nav.MenuItems.Add(row);
        }
        if (g.SeeAll is { } all)
        {
            var row = Item(all, new GalleryRow(null), null);
            AutomationProperties.SetAutomationId(row, "home.recipe-strip.all");
            Nav.MenuItems.Add(row);
        }
    }

    NavigationViewItem Item(string words, GalleryRow tag, string? glyph)
    {
        var row = new NavigationViewItem { Content = words, Tag = tag, Style = (Style)Application.Current.Resources["TafsSidebarItem"] };
        if (glyph is not null) row.Icon = new FontIcon { Glyph = glyph, Style = (Style)Application.Current.Resources["TafsSidebarIcon"] };
        AutomationProperties.SetName(row, words);
        return row;
    }

    /// <summary>
    /// The gallery, to browse, as the iPad's sheet from Explore jobs: a job's "Try it with Coach", for someone who may use
    /// Coach, starts a new conversation with the job's words in the box, as the iPad's <c>handRecipeToCoach</c>; nothing is
    /// sent. A strip job opens on its own sheet.
    /// </summary>
    void OpenGallery(GalleryRow row)
    {
        if (gallery is not { } g) return;
        Pages.Navigate(typeof(GalleryPage), new GalleryOpening(g, Browse: true,
            Hand: CoachMode.Answer is not null ? job => CoachMode.Write(job.Say, newConversation: true) : null, Open: row.Job, Logos: HomeList.Api));
    }
}

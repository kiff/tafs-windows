using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Home;
using Tafs.Core.NeedsYou;

namespace Tafs.App;

/// <summary>A held call on the Overview, under NEEDS YOU.</summary>
public sealed record HeldLine(string Title, string AnswerBy);

public sealed partial class OverviewPage : Page
{
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
        // No network yet: the tafs list comes from tafs cloud in its own change.
        Empty.Text = lines.Empty;
        Footnote.Text = lines.Footnote;
    }

    /// <summary>The held calls, when there are some: NEEDS YOU above the tafs, each with when to answer by.</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        var held = e.Parameter as IReadOnlyList<HeldCall> ?? [];
        NeedsYouLabel.Text = Sidebar.Eyebrow(w, "needs_you.title");
        Held.ItemsSource = held.Select(c => NeedsYouScreen.OverviewRow(w, c, TimeZoneInfo.Local))
            .Select(r => new HeldLine(r.Title, r.AnswerBy)).ToList();
        NeedsYou.Visibility = held.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

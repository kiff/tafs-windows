using Microsoft.UI.Xaml.Controls;

namespace Tafs.App;

public sealed partial class OverviewPage : Page
{
    public OverviewPage()
    {
        InitializeComponent();
        var w = App.Words;
        Heading.Text = w.Get("home.overview.title");
        Subtitle.Text = w.Get("home.overview.subtitle");
        ColumnTaf.Text = w.Get("home.overview.column_taf");
        ColumnNow.Text = w.Get("home.overview.column_now");
        ColumnCard.Text = w.Get("home.overview.column_card");
        ColumnLast.Text = w.Get("home.overview.column_last_result");
        // No network yet: the tafs list comes from tafs cloud in the next change.
        Empty.Text = w.Get("home.no_tafs");
        Footnote.Text = w.Get("home.overview.each_line_opens");
    }
}

using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Home;

namespace Tafs.App;

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
}

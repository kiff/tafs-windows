using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Connections;

namespace Tafs.App;

/// <summary>The table's four widths, shared by every row of a page so the columns line up (the iPad's ConnectColumns).</summary>
public sealed class ConnectColumnsView : INotifyPropertyChanged
{
    public GridLength App { get; private set; } = new(1, GridUnitType.Star);
    public GridLength How { get; private set; } = new(150);
    public GridLength Whose { get; private set; } = new(130);
    public GridLength Status { get; private set; } = new(230);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Fit(double width)
    {
        var w = ConnectAppsScreen.Widths(width);
        (App, How, Whose, Status) = (new(w[0]), new(w[1]), new(w[2]), new(w[3]));
        PropertyChanged?.Invoke(this, new(null)); // all four
    }
}

/// <summary>One row of the table as drawn: the column names, or an app.</summary>
public sealed class ConnectRowView
{
    public required ConnectColumnsView Columns { get; init; }
    public Thickness Divider { get; init; }
    public Visibility IsHeader { get; init; } = Visibility.Collapsed;
    public Visibility IsApp { get; init; } = Visibility.Visible;
    public string HeadApp { get; init; } = "", HeadHow { get; init; } = "", HeadWhose { get; init; } = "", HeadStatus { get; init; } = "";
    public string Name { get; init; } = "", Subtitle { get; init; } = "", How { get; init; } = "", Whose { get; init; } = "";
    public string Status { get; init; } = "", UsedBy { get; init; } = "", Setup { get; init; } = "", Button { get; init; } = "";
    public Visibility IsConnected { get; init; } = Visibility.Collapsed;
    public Visibility IsReconnect { get; init; } = Visibility.Collapsed;
    public Visibility HasButton { get; init; } = Visibility.Collapsed;
}

/// <summary>
/// Connect apps. tafs cloud's list comes in its own change: until then nothing is listed, or the iPad's sample.
/// Nothing here connects yet, so Connect stays dimmed.
/// </summary>
public sealed partial class ConnectAppsPage : Page
{
    readonly ConnectColumnsView columns = new();

    /// <summary>Not listed? Ask Coach.</summary>
    public static event Action? AskCoachChosen;

    public ConnectAppsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        Heading.Text = w.Get("connect.apps.title");
        YoursTitle.Text = w.Get("connect.apps.yours").ToUpper(new System.Globalization.CultureInfo(w.Language));
        DiscoverTitle.Text = w.Get("connect.apps.discover").ToUpper(new System.Globalization.CultureInfo(w.Language));
        AskCoach.Content = w.Get("connect.apps.not_listed");

        var list = e.Parameter as ConnectAppsList;
        var (yours, discover) = list is null ? ([], []) : ConnectAppsScreen.Split(list.Apps);
        // The column names head the first table shown, as on the iPad.
        Yours.ItemsSource = Rows(yours, header: true);
        Discover.ItemsSource = Rows(discover, header: yours.Count == 0);
        YoursTitle.Visibility = YoursPanel.Visibility = Seen(yours.Count > 0);
        DiscoverTitle.Visibility = DiscoverPanel.Visibility = Seen(discover.Count > 0);
        AskCoachPanel.Visibility = Seen(list?.ByKey == true);
    }

    static Visibility Seen(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    List<ConnectRowView> Rows(IReadOnlyList<ConnectApp> apps, bool header)
    {
        var w = App.Words;
        var rows = new List<ConnectRowView>();
        if (header && apps.Count > 0)
            rows.Add(new ConnectRowView
            {
                Columns = columns, IsHeader = Visibility.Visible, IsApp = Visibility.Collapsed,
                HeadApp = w.Get("connect.apps.column_app"), HeadHow = w.Get("connect.apps.column_how"),
                HeadWhose = w.Get("connect.apps.column_whose"), HeadStatus = w.Get("connect.apps.column_status"),
            });
        foreach (var app in apps)
        {
            var said = ConnectAppsScreen.Row(w, app);
            var button = ConnectAppsScreen.Button(w, app);
            rows.Add(new ConnectRowView
            {
                Columns = columns,
                Divider = new Thickness(0, rows.Count > 0 ? 1 : 0, 0, 0),
                Name = app.DisplayName, Subtitle = app.Subtitle ?? "",
                How = said.How, Whose = said.Whose ?? "",
                Status = said.Setup is null ? said.Status ?? "" : "", Setup = said.Setup ?? "",
                IsConnected = Seen(app.Connected && said.Setup is null), IsReconnect = Seen(app.NeedsReconnect && said.Setup is null),
                UsedBy = said.Setup is null ? said.UsedBy ?? "" : "",
                Button = button ?? "", HasButton = Seen(button is not null),
            });
        }
        return rows;
    }

    /// <summary>Both panels are as wide: the table's widths follow the page's.</summary>
    void OnPanelSized(object sender, SizeChangedEventArgs e)
    {
        var border = (Border)sender;
        columns.Fit(e.NewSize.Width - border.Padding.Left - border.Padding.Right);
    }

    void OnAskCoach(object sender, RoutedEventArgs e) => AskCoachChosen?.Invoke();
}

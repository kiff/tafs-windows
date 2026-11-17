using System.ComponentModel;
using System.Text.Json;
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
    public string HeadApp { get; init; } = "";
    public string HeadHow { get; init; } = "";
    public string HeadWhose { get; init; } = "";
    public string HeadStatus { get; init; } = "";
    public string Name { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string How { get; init; } = "";
    public string Whose { get; init; } = "";
    public string Status { get; init; } = "";
    public string UsedBy { get; init; } = "";
    public string Setup { get; init; } = "";
    public string Button { get; init; } = "";
    /// <summary>An app that needs setting up has its name in the soft colour, as on the iPad.</summary>
    public Visibility NameInk { get; init; } = Visibility.Visible;
    public Visibility NameSoft { get; init; } = Visibility.Collapsed;
    public Visibility HasUsedBy { get; init; } = Visibility.Collapsed;
    public Visibility HasSetup { get; init; } = Visibility.Collapsed;
    public double MinHeight { get; init; } = 52;
    public Visibility IsConnected { get; init; } = Visibility.Collapsed;
    public Visibility IsReconnect { get; init; } = Visibility.Collapsed;
    public Visibility HasButton { get; init; } = Visibility.Collapsed;
    /// <summary>Connect works: on a PC that approves, for an app connected by its sign-in, with a taf that needs it.</summary>
    public bool CanConnect { get; init; }
    public string ConnectName { get; init; } = "";
    public Microsoft.UI.Xaml.Media.ImageSource? Logo { get; init; }
    public Visibility HasLogo { get; init; } = Visibility.Collapsed;
    /// <summary>Its connections open (<see cref="AppConnections.Opens"/>), named "Your Stripe connections".</summary>
    public Visibility Opens { get; init; } = Visibility.Collapsed;
    public string OpensName { get; init; } = "";
    public ConnectApp? App { get; init; }
}

/// <summary>
/// An app's connections, from Connect apps (the iPad's <c>AppConnectionsView</c>): this PC's signed read of planque's grants,
/// the taf each was made for by its agent, whether this person manages Cards (who may end one), and the ending itself:
/// Windows Hello in the question's words, then planque's DELETE for each, signed by this device.
/// </summary>
public sealed record ConnectionsCalls(Func<Task<IReadOnlyList<Tafs.Core.Kiff.PlanqueGrant>?>> Grants, Func<Tafs.Core.Kiff.PlanqueGrant, string?> TafOf,
    bool Manages, Func<IReadOnlyList<string>, string, Task<(int Left, string? Why, bool Stopped)>> End);

/// <summary>
/// How Connect apps opens: the iPad's sample list for a screenshot, or tafs cloud's read as the session with this PC's
/// signed grants read (the proof, as Setup sends it); with neither, only its title.
/// </summary>
public sealed record ConnectAppsOpening(ConnectAppsList? Sample, Func<string, string?, Task<Tafs.Core.Taf.CloudRead>>? Read, Func<Task<string?>>? Proof,
    ConnectionsCalls? Connections = null, bool OpenFirst = false, Action? Back = null, ConnectAppsConnecting? Connecting = null);

/// <summary>
/// Connect apps: tafs cloud's list read as the session, searched as the person types, More apps for its next page; or the
/// iPad's sample. Connect stays dimmed until connecting is ported.
/// </summary>
public sealed partial class ConnectAppsPage : Page
{
    readonly ConnectColumnsView columns = new();
    ConnectAppsOpening? opening;
    ConnectAppsList? shown;
    /// <summary>The search shown, for its next page; only the newest read may replace the list, as the iPad's connectAppsRequest.</summary>
    string query = "";
    int request;
    CancellationTokenSource? typing;

    public static event Action? AskCoachChosen;

    public ConnectAppsPage() => InitializeComponent();

    void OnBackToAccount(object sender, RoutedEventArgs e) => opening?.Back?.Invoke();

    /// <summary>Each sign-out moves it on: the kept page then starts from nothing, even on Back, never with the last account's apps.</summary>
    static int forgotten;
    int seen;

    /// <summary>Signed out: the list kept in the cached page goes with the account.</summary>
    public static void Forget() => forgotten++;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        Heading.Text = w.Get("connect.apps.title");
        YoursTitle.Text = w.Get("connect.apps.yours");
        DiscoverTitle.Text = w.Get("connect.apps.discover");
        AskCoach.Content = w.Get("connect.apps.not_listed");
        More.Content = w.Get("connect.more_apps");
        Search.PlaceholderText = w.Get("connect.apps.search");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Search, w.Get("connect.apps.search"));
        // Kept in memory (the page is cached): back from an app's connections, the list is as it was, never read again
        // from the top; an ending there reads it itself.
        if (e.NavigationMode == NavigationMode.Back && shown is not null && seen == forgotten) return;
        var before = opening;
        opening = e.Parameter as ConnectAppsOpening;
        // From Account: Back to it.
        BackToAccount.Content = "‹ " + w.Get("common.back");
        BackToAccount.Visibility = opening?.Back is null ? Visibility.Collapsed : Visibility.Visible;
        // Opened again in the same session: the list kept shows at once, and is read again quietly over it. Another
        // session, or the sample, starts from nothing: one account's apps never show for another.
        var same = before?.Read is not null && ReferenceEquals(before.Read, opening?.Read) && shown is not null && query.Length == 0 && seen == forgotten;
        seen = forgotten;
        if (!same)
        {
            (grants, grantsRead, query) = ([], false, "");
            Search.Text = "";
            Show(opening?.Sample);
        }
        if (opening?.Read is not null)
        {
            Search.Visibility = Visibility.Visible;
            _ = ReadAsync("");
        }
    }

    /// <summary>
    /// The first page for what was typed, as the iPad's <c>loadConnectApps</c>: a new search empties the list while it's read;
    /// the proof goes with it, so tafs cloud can say which removed tafs' connections may be cleared. A read that fails says so.
    /// </summary>
    async Task ReadAsync(string q)
    {
        if (opening?.Read is not { } read) return;
        var mine = ++request;
        if (q != query || shown is null)
        {
            Show(null);
            Reading.Visibility = Visibility.Visible;
        }
        query = q;
        var proof = opening.Proof is { } signed ? await signed() : null;
        var answer = await read(ConnectAppsScreen.Path(q), proof);
        if (mine != request) return;
        Reading.Visibility = Visibility.Collapsed;
        if (answer.Answer is { } json) Show(ConnectAppsScreen.Read(json.GetRawText()));
        else Problem.Text = App.Words.Get("connect.apps_not_read", ("why", answer.Why ?? ""));
        Problem.Visibility = Seen(answer.Answer is null);
        // The account's connections, as this PC reads them, so a row opens its connections where it has any.
        if (opening.Connections is { } calls && await calls.Grants() is { } read2 && mine == request)
        {
            (grants, grantsRead) = (read2, true);
            Show(shown);
            // For a screenshot (--page connect-connections): the first app whose connections open.
            if (opening.OpenFirst && shown?.Apps.FirstOrDefault(a => AppConnections.Opens(a, grants)) is { } first)
                DispatcherQueue.TryEnqueue(() => Open(first, calls));
        }
    }

    /// <summary>The account's connections, as this PC last read them; none until read, or without the read.</summary>
    IReadOnlyList<Tafs.Core.Kiff.PlanqueGrant> grants = [];
    bool grantsRead;

    /// <summary>An app's connections, in this frame, with Back, as the iPad's NavigationLink.</summary>
    void OnOpenConnections(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ConnectRowView { App: { } app } && opening?.Connections is { } calls) Open(app, calls);
    }

    /// <summary>
    /// Connect, as the iPad's: "Which taf is this for?" among the tafs that need the app (asked only when there's more than one),
    /// tafs cloud starts the connection for it, then its approval sheet. Connected, the list is read again.
    /// </summary>
    async void OnConnect(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ConnectRowView { App: { } app } || opening?.Connecting is not { } connecting) return;
        var w = App.Words;
        var tafs = ConnectAppsScreen.ConnectableTafs(connecting.Tafs, app);
        if (tafs.Count == 0) return;
        var taf = tafs[0].Id;
        if (tafs.Count > 1)
        {
            var choices = new RadioButtons { SelectedIndex = 0 };
            foreach (var (_, name) in tafs) choices.Items.Add(name);
            var which = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("connect.which_taf"), Content = choices,
                PrimaryButtonText = w.Get("connect.apps.connect"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Primary,
            };
            if (await which.ShowAsync() != ContentDialogResult.Primary || choices.SelectedIndex < 0) return;
            taf = tafs[choices.SelectedIndex].Id;
        }
        Problem.Visibility = Visibility.Collapsed;
        var (started, why) = await connecting.Start(app, taf);
        if (started is not { } start)
        {
            if (why is not null) (Problem.Text, Problem.Visibility) = (why, Visibility.Visible);
            return;
        }
        if (start.NeedsKey) return;
        if (await ConnectDialog.ShowAsync(XamlRoot, ActualTheme, app, start.Grant, connecting)) await ReadAsync(query);
    }

    void Open(ConnectApp app, ConnectionsCalls calls)
    {
        if (shown is { } list)
            Frame.Navigate(typeof(AppConnectionsPage), new AppConnectionsOpening(app, list.RemovedTafs, grants, grantsRead, calls, async () =>
            {
                // Ended: the connections and the list are read again, as the iPad's disconnect does.
                await ReadAsync(query);
                return (grants, shown?.RemovedTafs ?? []);
            }));
    }

    /// <summary>More apps, as the iPad's <c>loadMoreConnectApps</c>: the same search's next page, added only if nothing changed meanwhile.</summary>
    async void OnMore(object sender, RoutedEventArgs e)
    {
        if (opening?.Read is not { } read || shown is not { NextCursor: { } cursor } before) return;
        var (mine, q) = (request, query);
        More.Content = App.Words.Get("common.loading");
        More.IsEnabled = false;
        var answer = await read(ConnectAppsScreen.Path(q, before.Provider, cursor), null);
        More.Content = App.Words.Get("connect.more_apps");
        More.IsEnabled = true;
        if (mine != request || q != query || shown?.NextCursor != cursor) return;
        if (answer.Answer is { } json) Show(ConnectAppsScreen.More(before, ConnectAppsScreen.Read(json.GetRawText())));
        else Problem.Text = App.Words.Get("connect.more_apps_could_not_listed", ("why", answer.Why ?? ""));
        Problem.Visibility = Seen(answer.Answer is null);
    }

    /// <summary>As the person types, a moment after the last key, as the iPad's .task(id: query): 0.3 s, at once when it's emptied.</summary>
    async void OnSearch(object sender, TextChangedEventArgs e)
    {
        typing?.Cancel();
        typing = new CancellationTokenSource();
        var token = typing.Token;
        var q = Search.Text;
        try { if (q.Length > 0) await Task.Delay(300, token); }
        catch (TaskCanceledException) { return; }
        await ReadAsync(q);
    }

    void Show(ConnectAppsList? list)
    {
        var w = App.Words;
        shown = list;
        var (yours, discover) = list is null ? ([], []) : ConnectAppsScreen.Split(list.Apps);
        // The column names head the first table shown, as on the iPad.
        Yours.ItemsSource = Rows(yours, header: true);
        Discover.ItemsSource = Rows(discover, header: yours.Count == 0);
        YoursTitle.Visibility = YoursPanel.Visibility = Seen(yours.Count > 0);
        DiscoverTitle.Visibility = DiscoverPanel.Visibility = Seen(discover.Count > 0);
        MakeOneText.Text = w.Get("connect.apps_connect_taf_make_one");
        MakeOne.Visibility = Seen(ConnectAppsScreen.SaysMakeATaf(discover.Count, connectableTafs: opening?.Connecting?.Tafs.Count ?? 0));
        AskCoachPanel.Visibility = Seen(list?.ByKey == true);
        MorePanel.Visibility = Seen(list?.NextCursor is not null);
    }

    static Visibility Seen(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    List<ConnectRowView> Rows(IReadOnlyList<ConnectApp> apps, bool header)
    {
        var w = App.Words;
        var rows = new List<ConnectRowView>();
        if (header && apps.Count > 0)
            rows.Add(new ConnectRowView
            {
                Columns = columns, IsHeader = Visibility.Visible, IsApp = Visibility.Collapsed, MinHeight = 0,
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
                Status = said.Setup is null ? said.Status ?? "" : "", Setup = said.Setup ?? "", HasSetup = Seen(!string.IsNullOrEmpty(said.Setup)),
                NameInk = Seen(!ConnectAppsScreen.NameIsSoft(app)), NameSoft = Seen(ConnectAppsScreen.NameIsSoft(app)),
                IsConnected = Seen(app.Connected && said.Setup is null), IsReconnect = Seen(app.NeedsReconnect && said.Setup is null),
                UsedBy = said.Setup is null ? said.UsedBy ?? "" : "", HasUsedBy = Seen(said.Setup is null && said.UsedBy is not null),
                Button = button ?? "", HasButton = Seen(button is not null),
                // Connecting by a key comes with its own change; by a sign-in, for a taf that needs the app.
                CanConnect = opening?.Connecting is { } connecting && !app.TakesKey && ConnectAppsScreen.ConnectableTafs(connecting.Tafs, app).Count > 0,
                ConnectName = (button ?? "") + ", " + app.DisplayName,
                Logo = ConnectAppsScreen.LogoUrl(app.Logo) is { } logo ? new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(logo) : null,
                HasLogo = Seen(ConnectAppsScreen.LogoUrl(app.Logo) is not null),
                Opens = Seen(opening?.Connections is not null && AppConnections.Opens(app, grants)),
                OpensName = w.Get("connect.apps.your_connections", ("app", app.DisplayName)),
                App = app,
            });
        }
        return rows;
    }

    void OnPanelSized(object sender, SizeChangedEventArgs e)
    {
        var border = (Border)sender;
        columns.Fit(e.NewSize.Width - border.Padding.Left - border.Padding.Right);
    }

    void OnAskCoach(object sender, RoutedEventArgs e) => AskCoachChosen?.Invoke();
}

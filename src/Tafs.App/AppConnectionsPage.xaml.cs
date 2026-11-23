using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Connections;
using Tafs.Core.Kiff;

namespace Tafs.App;

/// <summary>What an app's connections open with: the app, tafs cloud's removed tafs, this PC's read of the grants, and how to read again.</summary>
public sealed record AppConnectionsOpening(ConnectApp App, IReadOnlyList<RemovedTaf> Removed, IReadOnlyList<PlanqueGrant> Grants, bool Reads,
    ConnectionsCalls Calls, Func<Task<(IReadOnlyList<PlanqueGrant> Grants, IReadOnlyList<RemovedTaf> Removed)>> ReadAgain,
    ConnectAppsConnecting? Connecting = null);

/// <summary>
/// An app's connections, as the iPad's <c>AppConnectionsView</c>: a section per taf it was made for (those needing attention
/// first), then the removed tafs' with one Clear, then the others. Each is ended with Remove access, or Cancel while its
/// sign-in waits, asked first in words that name the taf it ends, then Windows Hello in the same words.
/// </summary>
public sealed partial class AppConnectionsPage : Page
{
    AppConnectionsOpening? opening;
    IReadOnlyList<PlanqueGrant> grants = [];
    IReadOnlyList<RemovedTaf> removed = [];
    /// <summary>The connection being ended, or Clear (its ids joined), one at a time; why the last one wasn't, or how Clear went.</summary>
    string? busy;
    string? note;
    bool noteIsError;
    /// <summary>The connection whose page is open (planque's row opened it), as the iPad's <c>ConnectionDetailView</c>; none, the list.</summary>
    string? detail;

    public AppConnectionsPage()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Show();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        opening = e.Parameter as AppConnectionsOpening;
        if (opening is null) return;
        (grants, removed) = (opening.Grants, opening.Removed);
        BackButton.Content = "‹ " + App.Words.Get("common.back");
        Heading.Text = App.Words.Get("connect.apps.your_connections", ("app", opening.App.DisplayName));
        Show();
    }

    void OnBack(object sender, RoutedEventArgs e)
    {
        // A connection's page goes back to the app's connections; the connections, to Connect apps.
        if (detail is not null)
        {
            (detail, note) = (null, null);
            Show();
            return;
        }
        if (Frame.CanGoBack) Frame.GoBack();
    }

    void Show()
    {
        if (opening is not { } o) return;
        var w = App.Words;
        Body.Children.Clear();
        if (detail is { } id)
        {
            // Its page, while planque still lists it; one gone meanwhile goes back to the list.
            if (grants.FirstOrDefault(g => g.Id == id) is { } open)
            {
                ShowDetail(open);
                return;
            }
            detail = null;
        }
        Heading.Text = w.Get("connect.apps.your_connections", ("app", o.App.DisplayName));
        var listed = AppConnections.RemovedListed(removed);
        var (tafs, gone, others) = AppConnections.Groups(AppConnections.Of(o.App, grants), o.Calls.TafOf, removed.Select(t => t.Agent).ToHashSet());
        // This PC hasn't read the account's connections: it says so, rather than an empty list.
        if (!o.Reads) Body.Children.Add(Words(w.Get("connect.start_connected_tools_first", ("device", w.Get("device.windows"))), 15, "TafsSoft"));
        foreach (var group in tafs)
            Section(group.Taf, group.Grants.Select(g => Row(g, group.Taf)));
        if (gone.Count > 0)
        {
            var rows = gone.Select(g => RemovedRow(g, listed.TryGetValue(g.Id, out var r) ? r.UsedBy.Select(u => u.Name).ToList() : null)).ToList();
            // Only one tafs cloud lists with nobody using it is cleared; not listed means no Clear.
            var clearable = gone.Where(g => listed.TryGetValue(g.Id, out var r) && r.UsedBy.Count == 0).Select(g => g.Id).ToList();
            if (o.Calls.Manages && clearable.Count > 0)
            {
                var clear = new Button
                {
                    Content = busy == "clear" ? w.Get("connect.apps.clearing") : w.Get("connect.clear_removed.button"),
                    Style = (Style)Application.Current.Resources["TafsOutlineNo"], IsEnabled = busy is null, Padding = new Thickness(16, 0, 16, 0),
                };
                AutomationProperties.SetAutomationId(clear, "connect.clear-removed");
                clear.Click += (_, _) => _ = ClearAsync(clearable);
                rows.Add(clear);
            }
            Section(w.Get("connect.from_removed"), rows);
        }
        if (others.Count > 0) Section(w.Get("connect.other_connections"), others.Select(g => Row(g, null)));
        if (o.Calls.Manages && tafs.Count > 0) Body.Children.Add(Words(w.Get("connect.apps.disconnect_why"), 13, "TafsDim"));
        if (note is { } n) Body.Children.Add(Words(n, 13, noteIsError ? "TafsNo" : "TafsInk"));
    }

    void Section(string title, IEnumerable<UIElement> rows)
    {
        if (title.Length > 0) Body.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["TafsEyebrow"], Margin = new Thickness(0, 8, 0, 0) });
        var panel = new StackPanel { Spacing = 0 };
        var first = true;
        foreach (var row in rows)
        {
            panel.Children.Add(new Border
            {
                Child = row, Padding = new Thickness(16, 12, 16, 12), BorderBrush = Look("TafsLine"),
                BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0),
            });
            first = false;
        }
        Body.Children.Add(new Border { Child = panel, CornerRadius = new CornerRadius(18), Background = Look("TafsPanel"), BorderBrush = Look("TafsLine"), BorderThickness = new Thickness(1) });
    }

    /// <summary>
    /// A connection, as the iPad's <c>row</c>: its name, its status in planque's words (or waiting on its sign-in), a word when
    /// it was made under a taf that doesn't need the app, how to fix it (Sign in again, or Replace the key; for a provider's
    /// connection, why it can't be signed in again in place), and Remove access or Cancel.
    /// </summary>
    UIElement Row(PlanqueGrant g, string? taf)
    {
        var w = App.Words;
        var opening = this.opening!;
        if (AppConnections.RowFromPlanque(g)) return PlanqueRow(g);
        var lines = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Words(g.Title ?? opening.App.DisplayName, 16, "TafsInk", semibold: true));
        // The other tafs whose Card draws on its tools, as the iPad's alsoOnCards: removing it stops them too.
        if (AppConnections.AlsoOn(w, opening.Calls.AlsoStops?.Invoke(g) ?? []) is { } also) lines.Children.Add(Words(also, 13, "TafsSoft"));
        if (g.StatusWords is { Length: > 0 } status) lines.Children.Add(Words(status, 13, "TafsDim"));
        else if (AppConnections.WaitingSignIn(g)) lines.Children.Add(Words(w.Get("connect.status.waiting_sign_in", ("vendor", g.Vendor)), 13, "TafsDim"));
        // Made under a taf that doesn't need the app (tafs cloud's misplaced): said, so it can be removed.
        if (opening.App.Misplaced.FirstOrDefault(m => m.Grant == g.Id) is { } wrong)
        {
            var said = Words(w.Get("connect.misplaced", ("taf", wrong.Name), ("app", opening.App.DisplayName)), 13, "TafsAmberText");
            AutomationProperties.SetAutomationId(said, "connect.misplaced." + g.Id);
            lines.Children.Add(said);
        }
        if (opening.Calls.Manages && g.SignsInThroughProvider)
            // A provider's connection isn't signed in again in place: said before any Windows Hello.
            lines.Children.Add(Words(w.Get("connect.through_provider", ("vendor", g.Vendor)), 13, "TafsSoft"));
        else if (opening.Calls.Manages && !AppConnections.Cancellable(g) && opening.Connecting is not null)
        {
            var word = w.Get(g.TakesKey ? "connect.replace_key" : "connect.sign_in_again");
            var fix = new Button
            {
                Content = word, IsEnabled = busy is null, Padding = new Thickness(0, 2, 0, 2), Margin = new Thickness(0, 2, 0, 0),
                Style = (Style)Application.Current.Resources["TafsQuietButton"], Foreground = Look("TafsAmberText"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            };
            AutomationProperties.SetAutomationId(fix, "connect.fix." + g.Id);
            AutomationProperties.SetName(fix, $"{word}: {g.Title ?? opening.App.DisplayName}");
            fix.Click += (_, _) => _ = FixAsync(g);
            lines.Children.Add(fix);
        }
        var row = new Grid { ColumnSpacing = 12, MinHeight = 44 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(lines);
        // Only someone who manages Cards ends a connection, as the iPad's canManageCards.
        if (opening.Calls.Manages)
        {
            var end = new Button
            {
                Content = busy == g.Id ? w.Get("connect.disconnecting") : AppConnections.EndWord(w, g), IsEnabled = busy is null,
                Style = (Style)Application.Current.Resources["TafsOutlineNo"], Padding = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetAutomationId(end, "connect.disconnect." + g.Id);
            AutomationProperties.SetName(end, $"{AppConnections.EndWord(w, g)}: {g.Title ?? opening.App.DisplayName}");
            end.Click += (_, _) => _ = EndAsync(g, taf);
            Grid.SetColumn(end, 1);
            row.Children.Add(end);
        }
        return row;
    }

    /// <summary>
    /// A connection as planque draws it, when it sends its actions (the iPad's <c>entry</c>): its name, the tafs that use it, its
    /// health, a word when it was made under a taf that doesn't need the app; the row opens its page, where its actions are.
    /// </summary>
    UIElement PlanqueRow(PlanqueGrant g)
    {
        var w = App.Words;
        var app = opening!.App;
        var lines = new StackPanel { Spacing = 2 };
        lines.Children.Add(Words(g.Title ?? app.DisplayName, 16, "TafsInk", semibold: true));
        if (g.UsedBy is { Count: > 0 } used) lines.Children.Add(Words(w.Get("connect.apps.used_by", ("tafs", string.Join(", ", used))), 13, "TafsSoft"));
        if (g.Health is { } health) lines.Children.Add(Words(health.Words, 13, health.Ok ? "TafsDim" : "TafsAmberText"));
        if (app.Misplaced.FirstOrDefault(m => m.Grant == g.Id) is { } wrong)
            lines.Children.Add(Words(w.Get("connect.misplaced", ("taf", wrong.Name), ("app", app.DisplayName)), 13, "TafsAmberText"));
        var grid = new Grid { ColumnSpacing = 12, MinHeight = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(lines);
        var chevron = Words("›", 18, "TafsSoft");
        chevron.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(chevron, 1);
        grid.Children.Add(chevron);
        var open = new Button
        {
            Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Style = (Style)Application.Current.Resources["TafsQuietButton"], Padding = new Thickness(0),
        };
        AutomationProperties.SetAutomationId(open, "connect.row." + g.Id);
        AutomationProperties.SetName(open, g.Title ?? app.DisplayName);
        open.Click += (_, _) =>
        {
            (detail, note) = (g.Id, null);
            Show();
        };
        return open;
    }

    /// <summary>
    /// A connection's page, as the iPad's <c>ConnectionDetailView</c>: its health, the tafs that use it, and for whoever manages
    /// Cards planque's known actions in its words and order. Remove asks first in planque's words, naming what stops; Sign in
    /// again shows what's approved first (by a key, the key sheet).
    /// </summary>
    void ShowDetail(PlanqueGrant g)
    {
        var w = App.Words;
        var o = opening!;
        Heading.Text = g.Title ?? o.App.DisplayName;
        var lines = new StackPanel { Spacing = 6 };
        if (g.Health is { } health) lines.Children.Add(Words(health.Words, 15, health.Ok ? "TafsInk" : "TafsAmberText", semibold: true));
        if (g.UsedBy is { Count: > 0 } used) lines.Children.Add(Words(w.Get("connect.apps.used_by", ("tafs", string.Join(", ", used))), 14, "TafsSoft"));
        if (lines.Children.Count > 0) Section("", [lines]);
        var actions = AppConnections.KnownActions(g);
        if (o.Calls.Manages && actions.Count > 0)
        {
            var rows = new List<UIElement>();
            foreach (var (action, i) in actions.Select((a, i) => (a, i)))
            {
                var removes = AppConnections.Removes(action);
                var b = new Button
                {
                    Content = busy == g.Id && removes ? w.Get("connect.disconnecting") : action.Words, IsEnabled = busy is null,
                    Style = (Style)Application.Current.Resources[removes ? "TafsOutlineNo" : "TafsOutlineButton"], Padding = new Thickness(16, 0, 16, 0),
                };
                AutomationProperties.SetAutomationId(b, $"connect.action.{action.Id}.{i}");
                b.Click += (_, _) => _ = TakeAsync(g, action);
                rows.Add(b);
            }
            Section("", rows);
        }
        if (note is { } n) Body.Children.Add(Words(n, 13, noteIsError ? "TafsNo" : "TafsInk"));
    }

    /// <summary>One of planque's actions on a connection's page: Remove after its question; Sign in again, by approve or by a key.</summary>
    async Task TakeAsync(PlanqueGrant g, GrantAction action)
    {
        if (opening is not { } o || busy is not null) return;
        if (!AppConnections.Removes(action))
        {
            await FixAsync(g, key: action.How == "key");
            return;
        }
        var w = App.Words;
        // One more ask, in planque's words, naming what stops; only then Windows Hello, in the same words.
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = action.Words, RequestedTheme = ActualTheme,
            Content = action.Stops is { } stops ? new TextBlock { Text = stops, TextWrapping = TextWrapping.Wrap } : null,
            PrimaryButtonText = action.Words, CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
        };
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
        (busy, note) = (g.Id, null);
        Show();
        var (left, why, stopped) = await o.Calls.End([g.Id], action.Words);
        if (stopped) return;
        // Removed: back to the app's connections, read again; else why stays on its page.
        if (left == 0) detail = null;
        await AfterAsync(left == 0 ? null : why, isError: true);
    }

    /// <summary>A removed taf's connection: what it is, the removed taf by the name planque keeps, its status, and who keeps it.</summary>
    UIElement RemovedRow(PlanqueGrant g, IReadOnlyList<string>? usedBy)
    {
        var lines = new StackPanel { Spacing = 2, MinHeight = 44 };
        lines.Children.Add(Words(g.Title ?? opening!.App.DisplayName, 16, "TafsInk", semibold: true));
        if (g.AgentName is { } name && name.Trim().Length > 0) lines.Children.Add(Words(name, 13, "TafsSoft"));
        if (g.StatusWords is { Length: > 0 } status) lines.Children.Add(Words(status, 13, "TafsDim"));
        if (usedBy is not null && AppConnections.KeptBy(App.Words, usedBy) is { } kept) lines.Children.Add(Words(kept, 13, "TafsSoft"));
        return lines;
    }

    /// <summary>
    /// Remove access, or Cancel for a sign-in that waits: asked first, naming the one taf it ends; then Windows Hello in the
    /// same words, then planque, signed by this device. Then the connections and Connect apps are read again.
    /// </summary>
    async Task EndAsync(PlanqueGrant g, string? taf)
    {
        if (opening is not { } o || busy is not null) return;
        var w = App.Words;
        var cancel = AppConnections.Cancellable(g);
        var alsoStops = o.Calls.AlsoStops?.Invoke(g) ?? [];
        var title = cancel ? AppConnections.CancelTitle(w, g) : AppConnections.DisconnectTitle(w, g.Vendor, taf, alsoStops);
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title, RequestedTheme = ActualTheme,
            Content = new TextBlock
            {
                Text = cancel ? w.Get("connect.cancel_sign_in.body", ("vendor", g.Vendor)) : AppConnections.DisconnectMessage(w, g.Vendor, taf, alsoStops, g.TakesKey),
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = cancel ? AppConnections.CancelWord(w, g) : w.Get("connect.disconnect_button"),
            CloseButtonText = cancel ? w.Get("connect.cancel_sign_in.keep_waiting") : w.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
        (busy, note) = (g.Id, null);
        Show();
        var (left, why, stopped) = await o.Calls.End([g.Id], title);
        if (stopped) return;
        await AfterAsync(left == 0 ? null : why, isError: true);
    }

    /// <summary>
    /// Sign in again: the connecting sheet over this connection, showing what's approved (its words and KIFF's for its profile)
    /// before Windows Hello, so nothing planque added since is approved unseen; then the same approval as connecting (a
    /// connected one signed in again in place; planque's <c>connect-fresh</c> where it asks for a new sign-in) and the vendor's
    /// page. Replace the key: the key sheet over this connection. Connected, the connections and the list are read again;
    /// otherwise the sheet said why.
    /// </summary>
    async Task FixAsync(PlanqueGrant g) => await FixAsync(g, g.TakesKey);

    async Task FixAsync(PlanqueGrant g, bool key)
    {
        if (opening is not { Connecting: { } connecting } o || busy is not null) return;
        var connected = key
            ? connecting.GiveKey is not null && await KeyDialog.ShowAsync(XamlRoot, ActualTheme, o.App, g.Id, connecting)
            : await ConnectDialog.ShowAsync(XamlRoot, ActualTheme, o.App, g.Id, connecting, again: true);
        if (connected)
        {
            (busy, note) = (g.Id, null);
            Show();
            await AfterAsync(null, isError: false);
        }
    }

    /// <summary>Clear, as the iPad's: asked first, then one Windows Hello for all of them; one a live Card uses was kept by tafs cloud's word.</summary>
    async Task ClearAsync(IReadOnlyList<string> ids)
    {
        if (opening is not { } o || busy is not null) return;
        var w = App.Words;
        var plan = AppConnections.ClearPlan(ids, grants, AppConnections.RemovedListed(removed));
        if (plan.Ending.Count == 0) return;
        var title = w.Count("connect.clear_removed.title", plan.Ending.Count);
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title, RequestedTheme = ActualTheme,
            Content = new TextBlock { Text = w.Get("connect.clear_removed.body"), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = w.Get("connect.clear_removed.clear"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
        };
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
        (busy, note) = ("clear", null);
        Show();
        var (left, why, stopped) = await o.Calls.End(plan.Ending.Select(g => g.Id).ToList(), title);
        if (stopped) return;
        var keptFor = plan.KeptFor.SelectMany(k => k).Select(k => k.Name).ToList();
        // Not confirmed: nothing was cleared, and it says so.
        var said = left == plan.Ending.Count && why == w.Get("common.not_done_unconfirmed") ? why
            : AppConnections.Cleared(w, plan.Ending.Count - left, plan.KeptFor.Count, keptFor, left);
        await AfterAsync(said, isError: left > 0);
    }

    async Task AfterAsync(string? said, bool isError)
    {
        if (opening is not { } o) return;
        var (readGrants, readRemoved) = await o.ReadAgain();
        (grants, removed, busy, note, noteIsError) = (readGrants, readRemoved, null, said, isError);
        Show();
    }

    TextBlock Words(string text, double size, string brush, bool semibold = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = Look(brush),
        FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
    };

    Brush Look(string key) => ChatViews.ThemeBrush(ActualTheme, key);
}

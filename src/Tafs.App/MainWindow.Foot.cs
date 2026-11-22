using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Home;
using Tafs.Core.Taf;
using Tafs.Core.Usage;

namespace Tafs.App;

/// <summary>
/// The sidebar's foot, as the iPad's <c>HomeSplitView.footer</c>: while Home can't be read, one quiet line saying so with the
/// status page beside it, and the app trying again by itself, less often, then stopping (the iPad's <c>refreshFailed</c>); and
/// the plan, only when it needs the person (tafs cloud's <c>home_line</c>), which opens Plan &amp; usage.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The waits between tries, in seconds, then no more until Home is read again some other way.</summary>
    static readonly int[] RetryWaits = [2, 10, 30, 60, 120];

    /// <summary>What a read of Home couldn't do, as the foot says it; none once a read goes through.</summary>
    string? unreachable;
    int retries;
    bool retryingRefresh;
    DispatcherTimer? retryTimer;

    /// <summary>tafs cloud's balance as last read, for the foot's plan line; none until it's read.</summary>
    Balance? footBalance;

    /// <summary>
    /// Home couldn't be read: said quietly at the foot, true to what failed (the network, or tafs cloud answering with an error),
    /// and read again a moment later, then less often, stopping after a few tries.
    /// </summary>
    void HomeReadFailed(Func<string, Task<CloudRead>> read, CloudRead failed)
    {
        var w = App.Words;
        unreachable = w.Get(failed.Status == 0 ? "home.cant_reach_kiff" : "home.couldnt_load_from_kiff");
        retryTimer?.Stop();
        retryingRefresh = retries < RetryWaits.Length;
        if (retryingRefresh)
        {
            var reading = sessions;
            retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(RetryWaits[retries++]) };
            retryTimer.Tick += (_, _) =>
            {
                retryTimer?.Stop();
                if (reading == sessions) _ = ReadHomeAsync(read);
            };
            retryTimer.Start();
        }
        ShowFoot();
    }

    /// <summary>Home was read: the foot's line goes, and the tries start again from the first wait next time.</summary>
    void HomeReadWent()
    {
        retryTimer?.Stop();
        (unreachable, retries, retryingRefresh) = (null, 0, false);
        ShowFoot();
    }

    /// <summary>tafs cloud's balance, read with Home, for the plan line; one that can't be read keeps what was read before.</summary>
    async Task ReadFootBalanceAsync(Func<string, Task<CloudRead>> read, int reading)
    {
        if (sample) return;
        var answer = await read("/v1/usage/balance");
        if (reading != sessions) return;
        if (answer.Answer is { } a && UsageScreen.ReadBalance(a.GetRawText()) is { } balance) footBalance = balance;
        ShowFoot();
    }

    /// <summary>Signed out: the foot says nothing until the next account's Home.</summary>
    void ForgetFoot()
    {
        retryTimer?.Stop();
        (unreachable, retries, retryingRefresh, footBalance) = (null, 0, false, null);
        signedOut = [];
        ShowFoot();
    }

    /// <summary>
    /// The tafs whose connection its vendor signed out, by agent, in planque's words for it (the iPad's <c>signedOut</c> and
    /// <c>signedOutWords</c>): it can't use those tools until the person signs in again, so it waits on them in the list.
    /// </summary>
    Dictionary<string, string> signedOut = [];

    /// <summary>The account's connections read with Home, as the iPad's refresh reads them; a read that fails keeps the last.</summary>
    async Task ReadSignedOutAsync(int reading)
    {
        if (sample || App.Sessions.Kept() is not { } session) return;
        var grants = await GrantsAsync(session);
        if (reading != sessions || grants is null) return;
        var w = App.Words;
        signedOut = grants.Where(g => g.NeedsReconnect && g.Request.Agent.Length > 0).GroupBy(g => g.Request.Agent)
            .ToDictionary(g => g.Key, g => g.First().StatusWords?.Trim() is { Length: > 0 } said ? said : w.Get("connect.status.signed_out_plain"));
        ShowYourTafs();
    }

    /// <summary>A Your tafs row's signed-out line, by its taf's agent; none when its connections are fine.</summary>
    string? SignedOutLine(string key) =>
        home?.Tafs.FirstOrDefault(t => t.Id == key)?.Agent is { } agent && signedOut.TryGetValue(agent, out var words) ? words : null;

    void ShowFoot()
    {
        var w = App.Words;
        UnreachableLine.Visibility = unreachable is null ? Visibility.Collapsed : Visibility.Visible;
        UnreachableText.Text = unreachable is { } words ? retryingRefresh ? w.Get("home.status_retrying", ("words", words)) : words : "";
        StatusLink.Content = w.Get("home.status");
        // Only when it needs the person: tafs cloud sends home_line from 80%, while the tafs wait, and with no plan.
        if (footBalance?.HomeLine is { Length: > 0 } line)
        {
            PlanText.Text = line;
            var tone = footBalance.PlanTone == Tone.Waiting ? "TafsNo" : "TafsAmberText";
            PlanText.Foreground = ChatViews.ThemeBrush(Root.ActualTheme, tone);
            PlanDot.Fill = PlanText.Foreground;
            PlanOpens.Text = w.Get("usage.title");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(PlanLine, w.Get("home.opens_plan", ("title", w.Get("usage.title"))));
            PlanLine.Visibility = Visibility.Visible;
        }
        else PlanLine.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Refreshing, as the iPad's pull to refresh on Home: F5 or Ctrl+R reads Home again (and with it the plan, the signed-out
    /// connections, Needs you and the Record), and starts the tries again after the foot's line stopped them.
    /// </summary>
    void ListenForRefresh()
    {
        void Add(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers)
        {
            var refresh = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = key, Modifiers = modifiers };
            refresh.Invoked += (accelerator, a) =>
            {
                a.Handled = true;
                if (cloudRead is not { } read) return;
                retries = 0;
                _ = ReadHomeAsync(read);
            };
            Root.KeyboardAccelerators.Add(refresh);
        }
        Add(Windows.System.VirtualKey.F5, Windows.System.VirtualKeyModifiers.None);
        Add(Windows.System.VirtualKey.R, Windows.System.VirtualKeyModifiers.Control);
    }

    /// <summary>The plan line opens Plan &amp; usage, as the iPad's <c>usageRequested</c>.</summary>
    void OnPlanLine(object sender, RoutedEventArgs e) =>
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.Usage);
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Home;

namespace Tafs.App;

/// <summary>
/// Your tafs' order and sections, as the iPad's <c>listedTafs</c> and <c>tafSections</c>: waiting first, then the one used
/// most recently (its latest call in the Record), then tafs cloud's order, settled so it doesn't move under the person; then,
/// under "Shared with you", the tafs shared with this person to use, not manage, as KIFF says.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The order the list was last settled in, by taf id, and the tafs waiting then (already placed by it).</summary>
    List<string> listOrder = [];
    HashSet<string> waitingWhenSettled = [];
    /// <summary>The tafs the order was settled for, and whether the Record had been read then.</summary>
    HashSet<string> settledFor = [];
    bool settledWithRecord;
    /// <summary>The window came back since: the order settles again at the next drawing.</summary>
    bool settleOnReturn;

    /// <summary>"Shared with you", between the person's own tafs and those shared with them; none without any.</summary>
    NavigationViewItemHeader? sharedHeader;

    /// <summary>Your tafs, in their order and sections, drawn again: after Home's read, the Record's, KIFF's access, or a return.</summary>
    void ShowYourTafs()
    {
        if (home is not { } listed) return;
        var w = App.Words;
        var yours = HomeList.YourTafs(w, listed, DateTimeOffset.UtcNow);
        var waiting = Waiting(listed);
        var latest = LatestUse(listed);
        var ids = listed.Tafs.Select(t => t.Id).ToHashSet();
        // Settled when the list's tafs change, the first time the Record is read, and when the window comes back.
        if (!ids.SetEquals(settledFor) || (!settledWithRecord && record.Count > 0) || settleOnReturn)
        {
            listOrder = ListOrder.ByRecent(listed.Tafs, t => t.Id, latest, waiting).Select(t => t.Id).ToList();
            (waitingWhenSettled, settledFor, settledWithRecord, settleOnReturn) = (waiting, ids, record.Count > 0, false);
        }
        var ordered = ListOrder.Listed(yours, r => r.Key, listOrder, latest, waiting, waitingWhenSettled);
        var (own, shared) = ListOrder.Split(ordered, r => Manages(listed, r.Key));
        AddYourTafs([.. own, .. shared], own.Count);
        YourTafs.Visibility = listed.Tafs.Count > 0 && yours.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The tafs waiting on the person: a call held for an answer (KIFF's, by its taf), or tafs cloud's own count.</summary>
    HashSet<string> Waiting(HomeAnswer listed)
    {
        var heldTafs = held.Select(h => h.Taf).ToHashSet();
        return listed.Tafs.Where(t => t.Waiting > 0 || t.State == "waiting" || heldTafs.Contains(t.Name)).Select(t => t.Id).ToHashSet();
    }

    /// <summary>Each taf's latest call in the Record, by its name on the line.</summary>
    Dictionary<string, DateTimeOffset> LatestUse(HomeAnswer listed)
    {
        var byName = listed.Tafs.GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First().Id);
        var latest = new Dictionary<string, DateTimeOffset>();
        foreach (var e in record)
            if (byName.TryGetValue(e.Taf, out var id) && (!latest.TryGetValue(id, out var at) || e.At > at)) latest[id] = e.At;
        return latest;
    }

    /// <summary>Whether this person manages the taf, as KIFF answered; none until it has, which counts as theirs.</summary>
    bool? Manages(HomeAnswer listed, string id) =>
        listed.Tafs.FirstOrDefault(t => t.Id == id)?.Agent is { } agent && manage is { } m && m.TryGetValue(agent, out var manages) ? manages : null;

    /// <summary>The window came back to the front: the order settles again, as the iPad's when the app comes back.</summary>
    void OnReturned(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated) return;
        settleOnReturn = true;
        ShowYourTafs();
        // Windows Hello may have been set up meanwhile: asked again with the steps, as the iPad reads its readiness on return.
        helloChecked = false;
        if (Pages.Content is NeedsYouPage) ShowSteps();
    }

    /// <summary>Signed out: the order is the next account's to settle.</summary>
    void ForgetOrder()
    {
        (listOrder, waitingWhenSettled, settledFor, settledWithRecord) = ([], [], [], false);
    }

    /// <summary>"Shared with you" at <paramref name="at"/> when there are shared tafs; gone otherwise.</summary>
    int PlaceSharedHeader(int at, bool shown)
    {
        if (!shown)
        {
            if (sharedHeader is not null) Nav.MenuItems.Remove(sharedHeader);
            return at;
        }
        sharedHeader ??= new NavigationViewItemHeader
        {
            Content = Sidebar.Eyebrow(App.Words, "home.shared_you"), Style = (Style)Application.Current.Resources["TafsSidebarHeader"],
        };
        var now = Nav.MenuItems.IndexOf(sharedHeader);
        if (now != at)
        {
            if (now >= 0) { Nav.MenuItems.RemoveAt(now); if (now < at) at--; }
            Nav.MenuItems.Insert(at, sharedHeader);
        }
        return at + 1;
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
        // Each open trial on its own row under its taf.
        var mine = WithTrials(w, listed, own);
        AddYourTafs([.. mine, .. shared], mine.Count);
        AddSettingUp(listed.SettingUp);
        YourTafs.Visibility = listed.Tafs.Count > 0 && yours.Count == 0 && listed.SettingUp.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A taf being set up's row: its name; Finish setting up when the row finishes it.</summary>
    sealed record SettingUpRow(SettingUp Taf);

    /// <summary>
    /// The tafs being set up, as the iPad's sidebar: among Your tafs, after the person's own and before Shared with you, by
    /// their name. Where the setup stopped, tafs cloud's line in amber; set up in someone else's chat, the name alone. A row
    /// whose chat is Coach's own, for someone who may use Coach, opens Coach there; any other opens nothing.
    /// </summary>
    void AddSettingUp(IReadOnlyList<SettingUp> settingUp)
    {
        foreach (var old in Nav.MenuItems.OfType<NavigationViewItem>().Where(i => i.Tag is SettingUpRow).ToList()) Nav.MenuItems.Remove(old);
        var own = Nav.MenuItems.OfType<NavigationViewItem>().Where(i => i.Tag is TafRow t && Manages(home!, t.Key) != false).LastOrDefault();
        var at = own is not null ? Nav.MenuItems.IndexOf(own) + 1 : Nav.MenuItems.IndexOf(YourTafs) + 1;
        foreach (var taf in settingUp)
        {
            var content = new Grid { ColumnSpacing = 12 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.Children.Add(new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = RecordLineView.Colour(taf.Colour),
                Child = new TextBlock { Text = Tafs.Core.Taf.TafScreen.Initial(taf.Name), FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var lines = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(new TextBlock { Text = taf.Name, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            if (taf.Line is { } line)
                lines.Children.Add(new TextBlock { Text = line, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
                    Style = (Style)Application.Current.Resources["TafsAmberWords"] });
            Grid.SetColumn(lines, 1);
            content.Children.Add(lines);
            // Discard, beside it, for the person whose setup it was and a PC that signs it (the iPad's DiscardSettingUpButton).
            if (taf.Discard is { } words && DiscardFor(taf) is { } discard)
            {
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var busy = taf.Agent is { } a && discarding.Contains(a);
                var button = new Button
                {
                    Content = busy ? App.Words.Get("common.loading") : words.Button, IsEnabled = !busy, VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["TafsOutlineButton"], FontSize = 13, Padding = new Thickness(10, 4, 10, 4),
                };
                AutomationProperties.SetAutomationId(button, "home.discard-setup");
                button.Click += (_, _) => _ = ConfirmDiscardAsync(taf, words, discard);
                Grid.SetColumn(button, 2);
                content.Children.Add(button);
            }
            var row = new NavigationViewItem
            {
                Content = content, Tag = new SettingUpRow(taf), Style = (Style)Application.Current.Resources["TafsSidebarItem"],
                HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 56, SelectsOnInvoked = false,
            };
            AutomationProperties.SetName(row, taf.Line is { } said ? $"{taf.Name}. {said}" : taf.Name);
            AutomationProperties.SetAutomationId(row, "home.finish-setup");
            Nav.MenuItems.Insert(at++, row);
        }
    }

    /// <summary>A taf being set up's row, invoked: Coach on the chat where its setup stopped, when the row finishes it.</summary>
    void OnSettingUpInvoked(SettingUpRow row)
    {
        if (row.Taf.Finish is { } finish && row.Taf.Finishes(CoachMode.CoachId, CoachMode.Answer is not null)) CoachMode.OpenOn(finish.Session);
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
        // Windows Hello may have been set up meanwhile: asked again, as the iPad reads its readiness on return; at launch too,
        // since the window's first activation comes here. Every approval's gate follows the answer. Not for a screenshot: it
        // draws a page and saves it, with no Windows Hello to ask (CI's runner has none that answers).
        if (screenshot is null) _ = CheckHelloAsync();
        // Devices read again, as the iPad's on its scene's return: a device added on the phone meanwhile shows.
        if (Pages.Content is DevicesPage devices) _ = devices.ReadAgainAsync();
        // A thread whose following stopped while its run still works is read once, and followed again if it moves.
        if (Pages.Content is TafPage taf) taf.CameBack();
        CoachMode.CameBack();
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

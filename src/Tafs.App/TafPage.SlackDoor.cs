using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// A Slack door's calls, for whoever manages the taf on a PC that approves with its passkey: the taf's Slack bot connections,
/// a connection's channels (read as this PC), adding the door (<see cref="SlackDoor.AddAsync"/>: planque subscribes behind
/// Windows Hello, then tafs cloud makes the door) and stopping a door's subscription before it's removed.
/// </summary>
public sealed record TafSlackDoor(Func<Task<IReadOnlyList<PlanqueGrant>?>> Bots,
    Func<string, Task<(IReadOnlyList<SlackChannel>? Channels, string? Why)>> Channels,
    Func<string, string, SlackChannel, Task<SlackDoorResult>> Add, Func<Door, Task<string?>> Stop);

public sealed partial class TafPage
{
    /// <summary>
    /// Adding a Slack door, as the iPad's <c>SlackDoorSheet</c>: one of the taf's Slack bot connections (the only one is
    /// picked for you), then one of its channels; a channel approves with Windows Hello, which has planque subscribe it, then
    /// tafs cloud makes the door, which opens. With no Slack bot connected, where to connect it.
    /// </summary>
    async Task AddSlackDoorAsync(DoorApp app, TafSlackDoor slack)
    {
        if (opening is null) return;
        var w = App.Words;
        var pane = new StackPanel { Spacing = 10 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("door.slack_door"),
            Content = new ScrollViewer { Content = pane, MaxHeight = 480 }, CloseButtonText = w.Get("common.cancel"),
        };
        PlanqueGrant? bot = null;
        IReadOnlyList<PlanqueGrant>? bots = null;
        IReadOnlyList<SlackChannel>? channels = null;
        string? why = null;
        var busy = false;
        string? addedDoor = null;

        void Draw()
        {
            pane.Children.Clear();
            if (bots is null)
            {
                pane.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left });
                return;
            }
            if (bots.Count == 0)
            {
                pane.Children.Add(Words(w.Get("door.slack.connect_first_in_connect_apps", ("taf", opening.Head.Name)), 15, "TafsSoftText"));
                return;
            }
            pane.Children.Add(new TextBlock { Text = w.Get("door.slack_workspace"), Style = Look("TafsEyebrow") });
            foreach (var b in bots)
            {
                var row = new Button
                {
                    // planque's title, else the app, as the iPad's name ?? vendor.
                    Content = (b.Id == bot?.Id ? "✓  " : "") + (b.Title?.Trim() is { Length: > 0 } title ? title : b.Vendor), Style = Look("TafsQuietButton"),
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12), IsEnabled = !busy,
                };
                row.Click += (_, _) => _ = PickAsync(b);
                pane.Children.Add(row);
            }
            if (bot is not null)
            {
                pane.Children.Add(new TextBlock { Text = w.Get("door.channel"), Style = Look("TafsEyebrow"), Margin = new Thickness(0, 6, 0, 0) });
                if (channels is null && why is null) pane.Children.Add(Words(w.Get("door.reading_channels"), 13, "TafsDimText"));
                else if (channels is { Count: 0 }) pane.Children.Add(Words(w.Get("door.slack_bot_sees_no_channel"), 14, "TafsDimText"));
                foreach (var c in channels ?? [])
                {
                    // A private channel with its lock, a public one with its #.
                    var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    line.Children.Add(new FontIcon { Glyph = c.Private ? "" : "", FontSize = 14 });
                    line.Children.Add(new TextBlock { Text = c.Name.Length == 0 ? c.Id : "#" + c.Name });
                    var row = new Button
                    {
                        Content = line, Style = Look("TafsQuietButton"), HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12), IsEnabled = !busy,
                    };
                    AutomationProperties.SetName(row, c.Place);
                    row.Click += (_, _) => _ = ApproveAsync(bot, c);
                    pane.Children.Add(row);
                }
            }
            if (busy) pane.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left });
            if (why is not null) pane.Children.Add(Words(why, 13, "TafsNoText"));
        }

        async Task PickAsync(PlanqueGrant picked)
        {
            (bot, channels, why) = (picked, null, null);
            Draw();
            var (read, failed) = await slack.Channels(picked.Id);
            // Another connection picked since: what was read for this one isn't drawn.
            if (bot?.Id != picked.Id) return;
            (channels, why) = (read, read is null ? w.Get("door.slack_channels_could_not_read", ("why", failed ?? w.Get("error.offline"))) : null);
            Draw();
        }

        async Task ApproveAsync(PlanqueGrant picked, SlackChannel channel)
        {
            if (busy) return;
            (busy, why) = (true, null);
            Draw();
            // Windows Hello names the act, in the words the iPad's Face ID prompt uses.
            using (HelloAct.Naming(w.Get("door.slack.let_read", ("taf", opening.Head.Name), ("place", channel.Place))))
            {
                var result = await slack.Add(app.Id, picked.Id, channel);
                busy = false;
                if (result.Added)
                {
                    addedDoor = app.Kind;
                    dialog.Hide();
                    return;
                }
                why = result.Why;
            }
            Draw();
        }

        Draw();
        var shown = dialog.ShowAsync();
        bots = await slack.Bots() ?? [];
        Draw();
        if (bots.Count == 1) _ = PickAsync(bots[0]);
        await shown;
        if (addedDoor is null) return;
        await LoadDoorsAsync();
        // The door just made opens, as Email's does.
        if (doors?.Doors.LastOrDefault(x => x.Kind == addedDoor) is { } added) (doorShown, setupScreen) = (added.Id, SetupScreen.Door);
        ShowSetup();
    }
}

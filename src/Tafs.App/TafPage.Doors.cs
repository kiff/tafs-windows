using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>
/// A taf's doors (P13), as the iPad's Setup › Starts when <c>DoorsList</c> and its door's own screen (<c>DoorsView.only</c>): where
/// work reaches the taf from outside the app, the answer going back the same way. Each door opens or closes, says its address
/// (Copy address for an email door) and its day; who's paired can give work, someone is paired or unpaired; the door is removed;
/// strangers' knocks wait for Do it, Pair or Ignore. A door is added from Starts when: email at once; WhatsApp and Slack are
/// added from tafs on an iPhone for now (their sign-up and planque's subscription aren't on Windows yet).
/// </summary>
public sealed partial class TafPage
{
    TafDoors? doors;
    string? doorsWhy;
    string? doorShown;
    bool doorsBusy;

    async Task LoadDoorsAsync()
    {
        if (opening is not { CloudId: { } id, Read: { } read, Manages: true } mine) return;
        var answer = await read(TafDoors.Path(id));
        if (opening != mine) return;
        if (answer.Answer is { } a && TafDoors.Read(a) is { } d) (doors, doorsWhy) = (d, null);
        else if (doors is null) doorsWhy = App.Words.Get("door.not_read");
        if (tab == TafTab.Setup && setupScreen is SetupScreen.StartsWhen or SetupScreen.Door) ShowSetup();
        // Its knocks on Activity too.
        ShowWork();
    }

    /// <summary>A change to a door, one at a time; the door it answers with replaces the one shown, else the doors are read again.</summary>
    async Task<bool> DoorChangeAsync(HttpMethod method, string path, byte[]? body, Func<string, string>? failed = null)
    {
        if (doorsBusy || opening?.Change is not { } change) return false;
        doorsBusy = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { ShowSetup(); ShowWork(); });
        var sent = await change(method, path, body);
        doorsBusy = false;
        if (sent.Answer is null)
        {
            var why = sent.Status == 429 && sent.Words is null ? App.Words.Get("door.knock.too_many") : sent.Why ?? App.Words.Get("error.ours");
            doorsWhy = failed is null ? why : failed(why);
            ShowSetup();
            ShowWork();
            return false;
        }
        doorsWhy = null;
        await LoadDoorsAsync();
        return true;
    }

    /// <summary>Starts when's Doors panel, for whoever manages the taf: each door with where it stands and Edit, then Add a door.</summary>
    void AddDoorsPanel()
    {
        if (opening is not { Manages: true, CloudId: not null }) return;
        var w = App.Words;
        var panel = new List<UIElement> { Eyebrow("setup.doors") };
        foreach (var door in doors?.Doors ?? [])
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var said = new StackPanel { Spacing = 2 };
            var label = Words(door.Label, 15, "TafsInkText");
            label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            said.Children.Add(label);
            said.Children.Add(Words(TafDoors.State(w, door), 13, "TafsSoftText"));
            row.Children.Add(said);
            var edit = new HyperlinkButton { Content = w.Get("setup.edit"), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(edit, w.Get("setup.edit") + ", " + w.Get("door.named", ("door", door.Label)));
            edit.Click += (_, _) => { (doorShown, setupScreen) = (door.Id, SetupScreen.Door); ShowSetup(); };
            Grid.SetColumn(edit, 1);
            row.Children.Add(edit);
            panel.Add(row);
        }
        // "+ Add a door…", an amber link, as the iPad's.
        var add = new HyperlinkButton
        {
            Padding = new Thickness(0), IsEnabled = doors is { } d && d.Addable.Any() && !doorsBusy,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "", FontSize = 14, Style = Look("TafsAmberIcon") },
                    new TextBlock { Text = w.Get("setup.add_door"), FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = Look("TafsAmberWords") },
                },
            },
        };
        AutomationProperties.SetName(add, w.Get("setup.add_door"));
        add.Click += (_, _) => _ = AddDoorAsync();
        panel.Add(add);
        if (doors is null && doorsWhy is null) panel.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left });
        if (doorsWhy is { } why && setupScreen == SetupScreen.StartsWhen) panel.Add(Words(why, 13, "TafsNoText"));
        foreach (var knock in doors?.OpenKnocks.Where(k => !k.IsLine) ?? []) panel.Add(KnockCard(knock));
        panel.Add(Words(w.Get("door.footer", ("taf", opening.Head.Name)), 12, "TafsDimText"));
        SetupPane.Children.Add(Panel([.. panel]));
    }

    /// <summary>
    /// Add a door: the apps that can still have one, those that know who's writing first. Email is added at once; the others
    /// say where they're added for now.
    /// </summary>
    async Task AddDoorAsync()
    {
        if (doors is not { } d || opening?.CloudId is not { } id) return;
        var w = App.Words;
        var list = new StackPanel { Spacing = 8 };
        DoorApp? chosen = null;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("door.add_door"),
            Content = new ScrollViewer { Content = list, MaxHeight = 480 }, CloseButtonText = w.Get("common.cancel"),
        };
        foreach (var (key, proven) in new[] { ("door.proven_section", true), ("door.unproven_section", false) })
        {
            var apps = d.Addable.Where(a => (a.Proof == "proven") == proven).ToList();
            if (apps.Count == 0) continue;
            list.Children.Add(new TextBlock { Text = w.Get(key), Style = Look("TafsEyebrow") });
            foreach (var app in apps)
            {
                var inside = new StackPanel { Spacing = 2 };
                var label = Words(app.Label, 15, "TafsInkText");
                label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                inside.Children.Add(label);
                inside.Children.Add(Words(app.How, 12, "TafsDimText"));
                var b = new Button { Content = inside, Style = Look("TafsQuietButton"), HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12) };
                AutomationProperties.SetName(b, app.Label);
                b.Click += (_, _) => { chosen = app; dialog.Hide(); };
                list.Children.Add(b);
            }
        }
        list.Children.Add(Words(w.Get("door.replies_only"), 12, "TafsDimText"));
        await dialog.ShowAsync();
        if (chosen is null) return;
        // WhatsApp: Meta's sign-up in the browser, its return taken only with this attempt's state.
        if (chosen.Kind == "whatsapp")
        {
            await AddWhatsAppDoorAsync(chosen, id);
            return;
        }
        // A Slack door: a connection, then a channel, approved with Windows Hello (the iPad's SlackDoorSheet).
        if (chosen.Kind == "slack" && opening.SlackDoor is { } slack)
        {
            await AddSlackDoorAsync(chosen, slack);
            return;
        }
        // A Slack door on a PC that can't approve: said where it's added.
        if (chosen.Kind != "email")
        {
            doorsWhy = w.Get(chosen.Kind == "slack" ? "door.slack.connect_first_in_connect_apps" : "door.whatsapp_elsewhere", ("taf", opening.Head.Name));
            ShowSetup();
            return;
        }
        if (await DoorChangeAsync(HttpMethod.Post, TafDoors.Path(id), TafDoors.AddBody(chosen.Id)) && doors?.Doors.FirstOrDefault(x => x.Kind == chosen.Kind) is { } added)
            (doorShown, setupScreen) = (added.Id, SetupScreen.Door);
        ShowSetup();
    }

    /// <summary>
    /// A door's own screen, as the iPad's <c>DoorsView.panel</c>: its name and where it stands with Open, its address and Copy
    /// address, its day; who's paired with Unpair and Pair someone, or that no one can be; Remove door; its knocks.
    /// </summary>
    void ShowDoor()
    {
        var w = App.Words;
        var named = doors?.Doors.FirstOrDefault(d => d.Id == doorShown);
        // In the page's place, the door's name in the bar; Back goes to Starts when, which it was opened from.
        ShowSubBar(named is { } d ? w.Get("door.named", ("door", d.Label)) : w.Get("setup.starts_when"));
        if (doors?.Doors.FirstOrDefault(d => d.Id == doorShown) is not { } door || opening?.CloudId is not { } id)
        {
            SetupPane.Children.Add(Words(w.Get(doors is null ? "common.loading" : "door.door_removed"), 15, "TafsSoftText"));
            return;
        }
        if (door.Words?.Settings is { } settings) SetupPane.Children.Add(Words(settings, 12, "TafsDimText"));
        if (doorsWhy is { } why) SetupPane.Children.Add(Words(why, 13, "TafsNoText"));
        // A door "From another application": its keys and signed changes, never the paired senders' panel or an unsigned removal.
        if (door.IsApi)
        {
            ShowApiDoor(door, id);
            return;
        }

        var state = new Grid { ColumnSpacing = 12 };
        state.ColumnDefinitions.Add(new ColumnDefinition());
        state.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        state.Children.Add(Words(TafDoors.State(w, door), 14, "TafsSoftText"));
        var open = new ToggleSwitch { IsOn = door.Open, IsEnabled = !doorsBusy, OnContent = "", OffContent = "", MinWidth = 0 };
        AutomationProperties.SetName(open, w.Get("door.status.open"));
        open.Toggled += (_, _) =>
        {
            if (open.IsOn == door.Open) return;
            var on = open.IsOn;
            _ = DoorChangeAsync(HttpMethod.Patch, TafDoors.DoorPath(id, door.Id), TafDoors.OpenBody(on),
                why => w.Get(on ? "door.not_opened" : "door.not_closed", ("why", why)));
        };
        Grid.SetColumn(open, 1);
        state.Children.Add(open);
        var address = Words(door.Address, 15, "TafsInkText");
        address.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas");
        address.IsTextSelectionEnabled = true;
        var top = new List<UIElement> { state, address };
        if (door.Kind == "email")
        {
            var copy = new Button { Content = w.Get("door.copy_address"), Style = Look("TafsOutlineButton") };
            copy.Click += async (_, _) =>
            {
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
                data.SetText(door.Address);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                copy.Content = w.Get("common.copied");
                await Task.Delay(1400);
                copy.Content = w.Get("door.copy_address");
            };
            top.Add(copy);
        }
        top.Add(new ProgressBar { Value = door.Limit > 0 ? 100.0 * Math.Min(door.Today, door.Limit) / door.Limit : 0, Height = 4 });
        foreach (var line in TafDoors.Day(w, door)) top.Add(Words(line, 12, "TafsDimText"));
        SetupPane.Children.Add(Panel([.. top]));

        var keys = new List<UIElement> { Eyebrow(door.HoldsKeys ? "door.key.paired" : "door.key.no_one_paired") };
        if (door.HoldsKeys)
        {
            foreach (var key in door.Keys)
            {
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var (name, under) = TafDoors.KeyShown(w, key, opening.Email);
                var who = new StackPanel { Spacing = 2 };
                var shown = Words(name, 15, "TafsInkText");
                shown.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                who.Children.Add(shown);
                if (under is not null) who.Children.Add(Words(under, 12, "TafsDimText"));
                row.Children.Add(who);
                if (!key.Auto)
                {
                    var unpair = new HyperlinkButton { Content = w.Get("door.unpair"), IsEnabled = !doorsBusy, VerticalAlignment = VerticalAlignment.Center };
                    AutomationProperties.SetName(unpair, w.Get("door.unpair") + ", " + name);
                    unpair.Click += (_, _) => _ = DoorChangeAsync(HttpMethod.Delete, TafDoors.KeyPath(id, door.Id, key.Id), null);
                    Grid.SetColumn(unpair, 1);
                    row.Children.Add(unpair);
                }
                keys.Add(row);
            }
            keys.Add(Words(w.Get("door.key.foot", ("how", door.How)), 12, "TafsDimText"));
            var pair = new Button { Content = w.Get("door.pair_someone"), Style = Look("TafsOutlineButton"), IsEnabled = !doorsBusy };
            pair.Click += (_, _) => _ = PairAsync(door);
            keys.Add(pair);
        }
        else keys.Add(Words(w.Get("door.key.none", ("how", door.How), ("taf", opening.Head.Name)), 15, "TafsInkText"));
        var remove = new Button { Content = w.Get("door.remove_door"), Style = Look("TafsOutlineNo"), IsEnabled = !doorsBusy };
        remove.Click += async (_, _) =>
        {
            var ask = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("door.remove_title", ("door", door.Label.ToLower(Tafs.Core.Words.Locale.Culture(w.Language)))),
                Content = new TextBlock { Text = w.Get("door.remove_body", ("taf", opening.Head.Name)), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = w.Get("door.remove"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
            };
            if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
            // A Slack door's subscription stops first, as the iPad's removeSlackDoor: one planque didn't stop keeps its door.
            if (door.Kind == "slack" && door.Trigger is not null)
            {
                if (opening.SlackDoor is not { } slack)
                {
                    doorsWhy = w.Get("door.start_connected_tools_first_slack", ("device", w.Get("device.windows")));
                    ShowSetup();
                    return;
                }
                if (await slack.Stop(door) is { } kept)
                {
                    doorsWhy = kept;
                    ShowSetup();
                    return;
                }
            }
            if (await DoorChangeAsync(HttpMethod.Delete, TafDoors.DoorPath(id, door.Id), null)) { setupScreen = SetupScreen.StartsWhen; ShowSetup(); }
        };
        keys.Add(remove);
        SetupPane.Children.Add(Panel([.. keys]));

        // Strangers' knocks wait on the person; a paired sender's message, kept from its knock, waits for Do it or Ignore.
        var knocks = doors.OpenKnocks.Where(k => k.Door == door.Id).ToList();
        if (knocks.Count > 0)
        {
            var panel = new List<UIElement> { Eyebrow("door.knocks_waiting") };
            panel.AddRange(knocks.Select(KnockCard));
            SetupPane.Children.Add(Panel([.. panel]));
        }
        SetupPane.Children.Add(Words(w.Get("door.footer", ("taf", opening.Head.Name)), 12, "TafsDimText"));
    }

    /// <summary>Pair someone: their address or number as the door names them, and a name if wanted.</summary>
    async Task PairAsync(Door door)
    {
        if (opening?.CloudId is not { } id) return;
        var w = App.Words;
        var sender = new TextBox
        {
            PlaceholderText = door.Kind switch
            {
                "email" => w.Get("door.pair_hint.email"),
                "whatsapp" => w.Get("door.pair_hint.phone"),
                "slack" => w.Get("door.pair_hint.slack"),
                _ => w.Get("door.pair_hint.other", ("door", door.Label)),
            },
            IsSpellCheckEnabled = false,
        };
        AutomationProperties.SetName(sender, sender.PlaceholderText);
        var name = new TextBox { PlaceholderText = w.Get("door.name_optional") };
        AutomationProperties.SetName(name, name.PlaceholderText);
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Words(w.Get("door.pair_intro", ("door", door.Label.ToLower(Tafs.Core.Words.Locale.Culture(w.Language)))), 14, "TafsSoftText"));
        body.Children.Add(sender);
        body.Children.Add(name);
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("door.pair_someone"), Content = body,
            PrimaryButtonText = w.Get("door.pair"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        sender.TextChanged += (_, _) => ask.IsPrimaryButtonEnabled = sender.Text.Trim().Length > 0;
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
        await DoorChangeAsync(HttpMethod.Post, TafDoors.KeysPath(id, door.Id), TafDoors.PairBody(sender.Text, name.Text));
    }

    /// <summary>
    /// A knock, as the iPad's <c>KnockCard</c>: by which app (and that it didn't check out), who wrote and about what, their words,
    /// then Do it (unless the sender check failed), Pair them (when it passed), and Ignore. A day's line has OK.
    /// </summary>
    UIElement KnockCard(Knock knock)
    {
        var w = App.Words;
        var card = new StackPanel { Spacing = 6, Padding = new Thickness(12) };
        var by = w.Get(knock.Paired ? "door.knock.paired_by" : "door.knock.by", ("app", knock.App.ToUpper(CultureInfo.InvariantCulture)))
            + (knock.Proof == "fail" ? " · " + w.Get("door.knock.didnt_check_out") : "");
        var stamp = Words(by, 10, knock.Paired ? "TafsSoftText" : "TafsAmberWords");
        stamp.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas");
        card.Children.Add(stamp);
        var lines = TafDoors.KnockLines(w, knock);
        var first = Words(lines[0], 14, "TafsInkText");
        first.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        card.Children.Add(first);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (knock.IsLine)
        {
            var ok = new Button { Content = w.Get("common.ok"), Style = Look("TafsOutlineButton"), IsEnabled = !doorsBusy };
            ok.Click += (_, _) => _ = AnswerKnockAsync(knock, "ignore", null);
            buttons.Children.Add(ok);
        }
        else
        {
            if (knock.Text.Length > 0)
            {
                var text = Words(knock.Text, 14, "TafsSoftText");
                text.IsTextSelectionEnabled = true;
                text.MaxLines = 8;
                card.Children.Add(text);
            }
            foreach (var line in lines.Skip(1)) card.Children.Add(Words(line, 12, "TafsDimText"));
            if (knock.Proof != "fail")
            {
                var doIt = new Button { Content = w.Get("door.do_it_button"), Style = Look("TafsPrimary"), IsEnabled = !doorsBusy };
                doIt.Click += (_, _) => _ = DoItAsync(knock);
                buttons.Children.Add(doIt);
            }
            if (knock.Proof == "pass" && !knock.Paired)
            {
                var first1 = knock.Name?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                var pair = new Button { Content = first1 is null ? w.Get("door.knock.pair_them") : w.Get("door.knock.pair", ("name", first1)),
                    Style = Look("TafsOutlineButton"), IsEnabled = !doorsBusy };
                pair.Click += (_, _) => _ = AnswerKnockAsync(knock, "pair", null);
                buttons.Children.Add(pair);
            }
            var ignore = new Button { Content = w.Get("door.ignore"), Style = Look("TafsQuietButton"), IsEnabled = !doorsBusy };
            ignore.Click += (_, _) => _ = AnswerKnockAsync(knock, "ignore", null);
            buttons.Children.Add(ignore);
        }
        card.Children.Add(buttons);
        return new Border
        {
            Child = card, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1),
            BorderBrush = knock.Paired ? ChatViews.ThemeBrush(ActualTheme, "TafsLine") : Faint("TafsAmber", 0.45),
            Background = knock.Paired ? ChatViews.ThemeBrush(ActualTheme, "TafsPanel") : Faint("TafsAmber", 0.07),
        };
    }

    Task<bool> AnswerKnockAsync(Knock knock, string action, string? say) =>
        opening?.CloudId is { } id ? DoorChangeAsync(HttpMethod.Post, TafDoors.KnockPath(id, knock.Id), TafDoors.AnswerBody(action, say),
            why => App.Words.Get("door.knock.not_sent", ("why", why))) : Task.FromResult(false);

    /// <summary>Do it: what the person tells the taf to do about a knock; their words are the instruction, the message only information.</summary>
    async Task DoItAsync(Knock knock)
    {
        var w = App.Words;
        var taf = opening?.Head.Name ?? "";
        var say = new TextBox { Text = w.Get("door.do_it.default"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110 };
        AutomationProperties.SetName(say, w.Get("door.what_do"));
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Words(w.Get("door.do_it.intro", ("who", knock.Who), ("taf", taf)), 14, "TafsSoftText"));
        body.Children.Add(say);
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("door.do_it.title", ("taf", taf)), Content = body,
            PrimaryButtonText = w.Get("door.send_to", ("taf", taf)), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Primary,
        };
        say.TextChanged += (_, _) => ask.IsPrimaryButtonEnabled = say.Text.Trim().Length > 0;
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
        await AnswerKnockAsync(knock, "do", say.Text);
    }
}

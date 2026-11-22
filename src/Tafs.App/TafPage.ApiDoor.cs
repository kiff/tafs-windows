using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// A door "From another application" (<c>api</c>, docs/doors.md), as the iPad's <c>APIDoorPanel</c>: today's count against its
/// cap, its last request in tafs cloud's words, its keys (at most two, each by its last four), New key, Revoke, the API
/// instructions and Remove. Every change but pausing is signed with one Windows Hello naming it
/// (<see cref="TafDoorSigning"/>); a key is shown once, kept out of screenshots and recordings, then only its last four.
/// </summary>
public sealed partial class TafPage
{
    /// <summary>The door's screen for an <c>api</c> door, in place of the paired senders' panel.</summary>
    void ShowApiDoor(Door door, string id)
    {
        var w = App.Words;
        var manages = opening?.Manages == true;
        var signing = opening?.DoorSigning;
        var zone = opening?.TimeZone is { } tz && TimeZoneInfo.TryFindSystemTimeZoneById(tz, out var z) ? z : TimeZoneInfo.Local;

        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new StackPanel { Spacing = 2 };
        title.Children.Add(new TextBlock { Text = w.Get("door.api.kind"), Style = Look("TafsEyebrow") });
        var label = Words(door.Label, 17, "TafsInkText");
        label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        title.Children.Add(label);
        head.Children.Add(title);
        if (manages)
        {
            // Pausing is never signed; opening it again is, with Windows Hello.
            var open = new ToggleSwitch { IsOn = door.Open, IsEnabled = !doorsBusy && (door.Open || signing is not null), OnContent = "", OffContent = "", MinWidth = 0 };
            AutomationProperties.SetName(open, w.Get("door.status.open"));
            AutomationProperties.SetAutomationId(open, "door.api.open");
            open.Toggled += async (_, _) =>
            {
                if (open.IsOn == door.Open) return;
                if (!open.IsOn)
                {
                    _ = DoorChangeAsync(HttpMethod.Patch, TafDoors.DoorPath(id, door.Id), TafDoors.OpenBody(false),
                        why => w.Get("door.not_closed", ("why", why)));
                    return;
                }
                if (!await SignedDoorAsync(HttpMethod.Patch, TafDoors.DoorPath(id, door.Id), ApiDoor.ReopenBody(),
                        w.Get("door.api.open_reason", ("app", door.Label))))
                    ShowSetup();
            };
            Grid.SetColumn(open, 1);
            head.Children.Add(open);
        }
        var top = new List<UIElement> { head };
        if (!door.Open) top.Add(Words(w.Get("door.api.paused"), 14, "TafsAmberWords"));
        top.Add(new TextBlock { Text = w.Get("door.api.today"), Style = Look("TafsEyebrow") });
        top.Add(new ProgressBar { Value = door.Limit > 0 ? 100.0 * Math.Min(ApiDoor.Today(door), door.Limit) / door.Limit : 0, Height = 6 });
        top.Add(Words(ApiDoor.OfLimit(w, door), 13, "TafsSoftText"));
        // Only in tafs cloud's words: the app never words a state itself, so none is drawn until they're sent.
        if (ApiDoor.LastRequestLine(w, door, zone) is { } last)
        {
            var line = Words(last, 13, "TafsSoftText");
            AutomationProperties.SetAutomationId(line, "door.api.last_request");
            top.Add(line);
        }
        SetupPane.Children.Add(Panel([.. top]));

        var keys = new List<UIElement> { new TextBlock { Text = w.Get("door.api.keys"), Style = Look("TafsEyebrow") } };
        foreach (var key in door.Keys)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var said = new StackPanel { Spacing = 2 };
            var name = Words(ApiDoor.Name(key), 15, "TafsInkText");
            name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            said.Children.Add(name);
            said.Children.Add(Words(ApiDoor.KeyLine(w, key, zone), 12, "TafsDimText"));
            row.Children.Add(said);
            if (manages && signing is not null)
            {
                var revoke = new HyperlinkButton { Content = w.Get("taf.revoke"), IsEnabled = !doorsBusy, VerticalAlignment = VerticalAlignment.Top,
                    Foreground = ChatViews.ThemeBrush(ActualTheme, "TafsNo") };
                AutomationProperties.SetName(revoke, w.Get("taf.revoke") + ", " + ApiDoor.Name(key));
                AutomationProperties.SetAutomationId(revoke, "door.api.revoke." + key.Id);
                revoke.Click += async (_, _) =>
                {
                    if (!await AskAsync(w.Get("door.api.revoke_title", ("key", ApiDoor.Name(key))), w.Get("door.api.revoke_body"), w.Get("taf.revoke"))) return;
                    await SignedDoorAsync(HttpMethod.Delete, TafDoors.KeyPath(id, door.Id, key.Id), ApiDoor.NoBody(),
                        w.Get("door.api.revoke_reason", ("key", ApiDoor.Name(key))));
                };
                Grid.SetColumn(revoke, 1);
                row.Children.Add(revoke);
            }
            keys.Add(row);
        }
        if (manages && signing is not null)
        {
            var newKey = new Button { Content = w.Get("door.api.new_key"), Style = Look("TafsOutlineButton"), IsEnabled = !doorsBusy && door.Keys.Count < ApiDoor.MaxKeys };
            AutomationProperties.SetAutomationId(newKey, "door.api.new_key");
            newKey.Click += (_, _) => _ = NewKeyAsync(door, id);
            keys.Add(newKey);
        }
        else if (manages) keys.Add(Words(w.Get("approval.device_can_not_approve_approve"), 13, "TafsAmberWords"));
        keys.Add(Words(w.Get("door.api.two_keys"), 12, "TafsDimText"));
        keys.Add(Words(w.Get("door.api.boundary", ("taf", opening?.Head.Name ?? "")), 13, "TafsSoftText"));
        var instructions = new HyperlinkButton { Content = w.Get("door.api.instructions"), Padding = new Thickness(0) };
        AutomationProperties.SetAutomationId(instructions, "door.api.instructions");
        instructions.Click += (_, _) => _ = InstructionsAsync(door);
        keys.Add(instructions);
        if (manages && signing is not null)
        {
            var remove = new Button { Content = w.Get("door.remove_door"), Style = Look("TafsOutlineNo"), IsEnabled = !doorsBusy };
            remove.Click += async (_, _) =>
            {
                var app = door.Label.ToLower(Tafs.Core.Words.Locale.Culture(w.Language));
                if (!await AskAsync(w.Get("door.remove_title", ("door", app)), w.Get("door.api.remove_body", ("app", door.Label), ("taf", opening?.Head.Name ?? "")),
                        w.Get("door.remove"))) return;
                if (await SignedDoorAsync(HttpMethod.Delete, TafDoors.DoorPath(id, door.Id), ApiDoor.NoBody(), w.Get("door.remove_title", ("door", app))))
                {
                    setupScreen = SetupScreen.StartsWhen;
                    ShowSetup();
                }
            };
            keys.Add(remove);
        }
        SetupPane.Children.Add(Panel([.. keys]));
        SetupPane.Children.Add(Words(w.Get("door.footer", ("taf", opening?.Head.Name ?? "")), 12, "TafsDimText"));
    }

    /// <summary>
    /// A signed change to the door, one at a time: Windows Hello naming <paramref name="act"/>, then the change. A closed
    /// Windows Hello sends nothing and says nothing; otherwise the doors are read again, or why it didn't go through.
    /// </summary>
    async Task<bool> SignedDoorAsync(HttpMethod method, string path, byte[] body, string act, Action<CloudRead>? answered = null)
    {
        if (doorsBusy || opening?.DoorSigning is not { } signing) return false;
        doorsBusy = true;
        ShowSetup();
        var sent = await signing.Send(method, path, body, act);
        doorsBusy = false;
        if (sent is null) { ShowSetup(); return false; }
        if (sent.Answer is null)
        {
            doorsWhy = sent.Why ?? App.Words.Get("error.ours");
            ShowSetup();
            return false;
        }
        doorsWhy = null;
        answered?.Invoke(sent);
        await LoadDoorsAsync();
        return true;
    }

    /// <summary>New key: its name (the door's own when left blank, at most 60), then Windows Hello; the key is then shown once.</summary>
    async Task NewKeyAsync(Door door, string id)
    {
        var w = App.Words;
        var name = new TextBox { PlaceholderText = w.Get("door.api.key_name_hint"), MaxLength = ApiDoor.MaxKeyName, IsSpellCheckEnabled = false,
            Header = w.Get("door.api.key_name") };
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Words(w.Get("door.api.two_keys"), 14, "TafsSoftText"));
        body.Children.Add(name);
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("door.api.new_key"), Content = body,
            PrimaryButtonText = w.Get("door.api.new_key"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Primary,
        };
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
        IssuedKey? issued = null;
        await SignedDoorAsync(HttpMethod.Post, TafDoors.KeysPath(id, door.Id), ApiDoor.NewKeyBody(ApiDoor.KeyName(name.Text, door)),
            w.Get("door.api.new_key_reason", ("app", door.Label)), sent => issued = sent.Answer is { } a ? ApiDoor.ReadIssued(a) : null);
        if (issued is { } key) await ShowKeyOnceAsync(key, door);
    }

    /// <summary>The key, shown this once (<see cref="ApiDoorDialogs.ShowKeyOnceAsync"/>).</summary>
    Task ShowKeyOnceAsync(IssuedKey key, Door door) =>
        ApiDoorDialogs.ShowKeyOnceAsync(XamlRoot, ActualTheme, key, door, opening?.DoorSigning?.Cloud ?? Tafs.Core.Home.HomeList.Api);

    /// <summary>API instructions, as the iPad's <c>APIInstructionsView</c>: everything a developer needs but the key.</summary>
    async Task InstructionsAsync(Door door)
    {
        var w = App.Words;
        var endpoint = ApiDoor.Endpoint(door, opening?.DoorSigning?.Cloud ?? Tafs.Core.Home.HomeList.Api);
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = w.Get("door.api.where"), Style = Look("TafsEyebrow") });
        body.Children.Add(Code(endpoint, wrap: true));
        body.Children.Add(new TextBlock { Text = w.Get("door.api.example"), Style = Look("TafsEyebrow") });
        body.Children.Add(Code(ApiDoor.Example(endpoint), wrap: false));
        body.Children.Add(Words(w.Get("door.api.key_not_here"), 14, "TafsSoftText"));
        body.Children.Add(Words(w.Get("door.api.boundary", ("taf", opening?.Head.Name ?? "")), 14, "TafsSoftText"));
        await new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("door.api.instructions"),
            Content = new ScrollViewer { Content = body, MaxHeight = 520 }, CloseButtonText = w.Get("common.done"),
        }.ShowAsync();
    }

    UIElement Code(string text, bool wrap) => ApiDoorDialogs.Code(text, wrap, ActualTheme);

    /// <summary>A question with its act and Cancel, Cancel the default.</summary>
    async Task<bool> AskAsync(string title, string body, string act) =>
        await new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = title, Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = act, CloseButtonText = App.Words.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
        }.ShowAsync() == ContentDialogResult.Primary;
}

/// <summary>
/// An <c>api</c> door's dialogs shared by a taf's page and Coach's drafted door card (the iPad's <c>IssuedKeyView</c> and
/// <c>CodeBlock</c>).
/// </summary>
static class ApiDoorDialogs
{
    static Style Look(string key) => (Style)Application.Current.Resources[key];

    static TextBlock Line(string text, double size, string style) =>
        new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Style = Look(style) };

    /// <summary>Code, as written: monospaced, selectable, scrolled sideways rather than wrapped (an address may wrap).</summary>
    public static UIElement Code(string text, bool wrap, ElementTheme theme)
    {
        var t = new TextBlock { Text = text, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, IsTextSelectionEnabled = true,
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, Style = Look("TafsInkText"),
            FlowDirection = FlowDirection.LeftToRight };
        return new Border
        {
            Child = new ScrollViewer { Content = t, HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = wrap ? ScrollMode.Disabled : ScrollMode.Enabled },
            Padding = new Thickness(12), CornerRadius = new CornerRadius(12), Background = ChatViews.ThemeBrush(theme, "TafsRaised"),
        };
    }

    /// <summary>
    /// The key, shown this once, as the iPad's <c>IssuedKeyView</c>: Copy (kept out of Windows' clipboard history and cloud
    /// clipboard, cleared after a minute), where to send work and an example that reads the key from the caller's own secret.
    /// While it shows, the window is kept out of screenshots, recordings and screen sharing; closing it forgets the key.
    /// </summary>
    public static async Task ShowKeyOnceAsync(XamlRoot root, ElementTheme theme, IssuedKey key, Door door, Uri cloud)
    {
        var w = App.Words;
        var endpoint = ApiDoor.Endpoint(door, cloud);
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(Line(w.Get("door.api.shown_once_body", ("app", door.Label)), 15, "TafsSoftText"));
        var shown = new TextBlock { Text = key.Key, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 14, IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = Look("TafsInkText") };
        AutomationProperties.SetAutomationId(shown, "door.api.key");
        body.Children.Add(new Border { Child = shown, Padding = new Thickness(12), CornerRadius = new CornerRadius(12), Background = ChatViews.ThemeBrush(theme, "TafsRaised") });
        var copy = new Button { Content = w.Get("door.api.copy_key"), Style = Look("TafsPrimary"), Padding = new Thickness(18, 8, 18, 8) };
        AutomationProperties.SetAutomationId(copy, "door.api.copy_key");
        copy.Click += (_, _) =>
        {
            SecretClipboard.Copy(key.Key);
            copy.Content = w.Get("door.api.copied");
        };
        body.Children.Add(copy);
        body.Children.Add(new TextBlock { Text = w.Get("door.api.where"), Style = Look("TafsEyebrow") });
        body.Children.Add(Code(endpoint, wrap: true, theme));
        body.Children.Add(new TextBlock { Text = w.Get("door.api.example"), Style = Look("TafsEyebrow") });
        body.Children.Add(Code(ApiDoor.Example(endpoint), wrap: false, theme));
        var dialog = new ContentDialog
        {
            XamlRoot = root, RequestedTheme = theme, Title = w.Get("door.api.shown_once"),
            Content = new ScrollViewer { Content = body, MaxHeight = 520 }, PrimaryButtonText = w.Get("common.done"), DefaultButton = ContentDialogButton.Primary,
        };
        // Escape doesn't close it: only Done, so the key isn't lost to a stray key press.
        dialog.Closing += (_, e) => { if (e.Result == ContentDialogResult.None) e.Cancel = true; };
        CaptureGuard.Exclude(true);
        try { await dialog.ShowAsync(); }
        finally { CaptureGuard.Exclude(false); }
    }
}

/// <summary>
/// The window kept out of screenshots, recordings and screen sharing while a secret shows, as Android's <c>FLAG_SECURE</c>:
/// Windows' <c>WDA_EXCLUDEFROMCAPTURE</c> (Windows 10 2004 and later; older Windows shows it black, <c>WDA_MONITOR</c>).
/// </summary>
static partial class CaptureGuard
{
    const uint None = 0, Monitor = 1, ExcludeFromCapture = 0x11;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    public static void Exclude(bool on)
    {
        var hwnd = App.WindowHandle;
        if (hwnd == IntPtr.Zero) return;
        if (!on) { SetWindowDisplayAffinity(hwnd, None); return; }
        if (!SetWindowDisplayAffinity(hwnd, ExcludeFromCapture)) SetWindowDisplayAffinity(hwnd, Monitor);
    }
}

/// <summary>
/// A secret copied for this PC only, as the iPad's <c>Clipboard.copySecret</c>: never in Windows' clipboard history or its cloud
/// clipboard, and taken off the clipboard after a minute unless something else was copied since.
/// </summary>
static class SecretClipboard
{
    public static void Copy(string secret)
    {
        var data = new Windows.ApplicationModel.DataTransfer.DataPackage();
        data.SetText(secret);
        var options = new Windows.ApplicationModel.DataTransfer.ClipboardContentOptions { IsAllowedInHistory = false, IsRoamable = false };
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContentWithOptions(data, options);
        _ = ClearLaterAsync(secret);
    }

    static async Task ClearLaterAsync(string secret)
    {
        await Task.Delay(TimeSpan.FromMinutes(1));
        try
        {
            var now = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (now.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text) && await now.GetTextAsync() == secret)
                Windows.ApplicationModel.DataTransfer.Clipboard.Clear();
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException) { }
    }
}

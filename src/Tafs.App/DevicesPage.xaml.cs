using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Account;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>How Devices reads KIFF as the session; none for a screenshot, which shows the list empty.</summary>
/// <param name="Signing">Signing another device out and removing a passkey; none on a PC that doesn't approve.</param>
public sealed record DevicesOpening(Func<string, Task<CloudRead>>? Read, DevicesSigning? Signing = null);

/// <summary>
/// What Devices signs on a PC whose own passkey approves (<see cref="AccountRemoval.RemoveAsync"/>): this PC's passkey
/// approving the exact <c>DELETE</c>, then sending it, while this sign-in is still the one; and whether this person may
/// remove a passkey (an owner or an admin).
/// </summary>
public sealed record DevicesSigning(Func<string, Task<(string Header, string Value)>> Sign,
    Func<string, (string Header, string Value), Task<CloudRead>> Delete, Func<bool> Current, bool MayRemovePasskeys);

/// <summary>
/// Account › Devices, as the iPad's <c>DevicesView</c>: each device signed in as this person, its name and when it was last
/// used, "This device" for this PC, Sign out for every other one, and the footer; then the account's passkeys, each by its
/// name, a computer's own going with its computer, and Remove for the person's own. Signing out and removing are approvals,
/// so each asks first ("Sign Laptop out?"), then Windows Hello, then reads again. A PC that can't approve shows neither, with
/// Android's line that says to use a device that can. A phone adds a computer and a passkey here; on Windows, this PC is the
/// computer, so neither is here.
/// </summary>
public sealed partial class DevicesPage : Page
{
    public DevicesPage() => InitializeComponent();

    static Style Look(string key) => (Style)Application.Current.Resources[key];

    DevicesOpening? opening;
    bool removing;

    /// <summary>Back to Account, where it opened from.</summary>
    void OnBack(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        BackButton.Content = "‹ " + App.Words.Get("common.back");
        var w = App.Words;
        Heading.Text = w.Get("settings.devices.title");
        Note.Text = w.Get("settings.devices.note_device", ("device", w.Get("device.windows")));
        opening = e.Parameter as DevicesOpening;
        if (opening?.Read is null)
        {
            Spinner.IsActive = false;
            Spinner.Visibility = Visibility.Collapsed;
            return;
        }
        await LoadAsync();
    }

    /// <summary>
    /// Read again when the window comes back (the iPad's on its scene's return), so a device or passkey added meanwhile shows
    /// without leaving Devices. Not while a removal is going: it reads again when it's done.
    /// </summary>
    public async Task ReadAgainAsync()
    {
        if (removing || reading) return;
        reading = true;
        try { await LoadAsync(); }
        finally { reading = false; }
    }

    bool reading;

    /// <summary>Devices, then the passkeys, each read again; a failed read says why in its place.</summary>
    async Task LoadAsync()
    {
        if (opening?.Read is not { } read) return;
        var w = App.Words;
        var answer = await read(Devices.Path);
        Spinner.IsActive = false;
        Spinner.Visibility = Visibility.Collapsed;
        if (answer.Answer is not { } json || Devices.Read(json) is not { } devices)
        {
            // KIFF's refusal in the iPad's plain words (KIFFError, PlainError), never KIFF's own reason.
            Error.Text = w.Get("settings.devices.not_loaded", ("why", answer.Answer is null ? Tafs.Core.Kiff.KiffErrors.Words(w, answer.Status) : w.Get("error.ours")));
            Error.Visibility = Visibility.Visible;
        }
        else Show(devices);

        var keys = await read(Passkeys.Path);
        PasskeysTitle.Text = w.Get("settings.devices.passkeys_title");
        PasskeysTitle.Visibility = Visibility.Visible;
        if (keys.Answer is not { } k || Passkeys.Read(k) is not { } passkeys)
        {
            PasskeysError.Text = w.Get("settings.devices.passkeys_not_loaded",
                ("why", keys.Answer is null ? Tafs.Core.Kiff.KiffErrors.Words(w, keys.Status) : w.Get("error.ours")));
            PasskeysError.Visibility = Visibility.Visible;
            PasskeysPanel.Visibility = Visibility.Collapsed;
            return;
        }
        ShowPasskeys(passkeys);
    }

    /// <summary>One row a device, a line between them: its name and when it was last used; This device, or Sign out for another.</summary>
    void Show(IReadOnlyList<Device> devices)
    {
        var w = App.Words;
        var now = DateTimeOffset.UtcNow;
        Rows.Children.Clear();
        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            var row = new Grid { MinHeight = 60, ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = Devices.Name(w, device), FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = Look("TafsInkText") };
            var used = new TextBlock { Text = Devices.LastUsed(w, device.LastUsed, now), FontSize = 13, Style = Look("TafsDimText") };
            var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { name, used } };
            row.Children.Add(words);
            if (device.ThisDevice)
            {
                var right = new TextBlock { Text = w.Get("settings.devices.this_device_title"), FontSize = 13, Style = Look("TafsDimText"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(right, 1);
                row.Children.Add(right);
            }
            else if (opening?.Signing is not null)
            {
                var signOut = Remove(w.Get("settings.devices.sign_out"), name.Text);
                signOut.Click += async (_, _) =>
                {
                    var named = device.Name is { } n ? w.Get("settings.devices.sign_out_named", ("device", n)) : w.Get("settings.devices.sign_out_this");
                    // Its last use under the question: two devices with one name read apart.
                    if (!await AskAsync(named, w.Get("settings.devices.sign_out"), used.Text)) return;
                    await RemoveAsync(Devices.DevicePath(device.Id),
                        w.Get("settings.devices.sign_out_prompt", ("device", device.Name ?? w.Get("settings.devices.this_device"))), Error);
                };
                Grid.SetColumn(signOut, 1);
                row.Children.Add(signOut);
            }
            AutomationProperties.SetName(row, $"{name.Text}. {used.Text}" + (device.ThisDevice ? $". {w.Get("settings.devices.this_device_title")}" : ""));
            Rows.Children.Add(new Border { Style = Look("TafsHourLine"), Child = row, Padding = new Thickness(16, 0, 16, 0),
                BorderThickness = new Thickness(0, i > 0 ? 1 : 0, 0, 0) });
        }
        Panel.Visibility = devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // A PC that can't approve signs nothing out: Android's line, where Sign out would be.
        NeedsApprover.Text = w.Get("settings.devices.signout_needs_approver");
        NeedsApprover.Visibility = opening?.Signing is null && devices.Any(d => !d.ThisDevice) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Each passkey by its name; a computer's own "Goes with its computer"; Remove for the person's own, for an owner or admin here.</summary>
    void ShowPasskeys(IReadOnlyList<AccountPasskey> passkeys)
    {
        var w = App.Words;
        PasskeyRows.Children.Clear();
        for (var i = 0; i < passkeys.Count; i++)
        {
            var passkey = passkeys[i];
            var row = new Grid { MinHeight = 52, ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = Passkeys.Name(w, passkey), FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = Look("TafsInkText") };
            var words = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { name } };
            if (!passkey.Removable) words.Children.Add(new TextBlock { Text = w.Get("settings.devices.passkey_with_computer"), FontSize = 13, Style = Look("TafsDimText") });
            row.Children.Add(words);
            if (passkey is { Removable: true, Id: { } id } && opening?.Signing is { MayRemovePasskeys: true })
            {
                var remove = Remove(w.Get("settings.devices.passkey_remove"), name.Text);
                remove.Click += async (_, _) =>
                {
                    // The last of the person's own that approves: said under the question, never refused.
                    if (!await AskAsync(w.Get("settings.devices.passkey_remove_named", ("name", name.Text)), w.Get("settings.devices.passkey_remove"),
                        Passkeys.Warning(w, passkey, passkeys))) return;
                    await RemoveAsync(Passkeys.RemovePath(id), w.Get("settings.devices.passkey_remove_prompt", ("name", name.Text)), PasskeysError);
                };
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
            }
            AutomationProperties.SetName(row, name.Text + (passkey.Removable ? "" : ". " + w.Get("settings.devices.passkey_with_computer")));
            PasskeyRows.Children.Add(new Border { Style = Look("TafsHourLine"), Child = row, Padding = new Thickness(16, 0, 16, 0),
                BorderThickness = new Thickness(0, i > 0 ? 1 : 0, 0, 0) });
        }
        PasskeysPanel.Visibility = passkeys.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    Button Remove(string words, string what)
    {
        var button = new Button { Content = words, Style = Look("TafsQuietButton"), Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TafsNo"],
            VerticalAlignment = VerticalAlignment.Center, IsEnabled = !removing };
        AutomationProperties.SetName(button, $"{words}, {what}");
        return button;
    }

    /// <summary>The iPad's confirmation: its question, then the act, or Cancel; <paramref name="detail"/> under the question.</summary>
    async Task<bool> AskAsync(string question, string act, string? detail = null)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = question, PrimaryButtonText = act,
            Content = detail is null ? null : new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Style = Look("TafsSoftText") },
            CloseButtonText = App.Words.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
        };
        // Sign out and Remove ask Windows Hello next: while it can't confirm here, why, and the act off.
        ApprovalGate.Notice(dialog);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>One removal at a time, signed behind Windows Hello; why it didn't go stays at the top of its list after the lists are read again.</summary>
    async Task RemoveAsync(string path, string what, TextBlock why)
    {
        if (removing || opening?.Signing is not { } signing) return;
        removing = true;
        (Error.Visibility, PasskeysError.Visibility) = (Visibility.Collapsed, Visibility.Collapsed);
        string? said;
        // Windows Hello names the act: "Sign {device} out of your account", or "Remove the passkey {name}".
        using var named = HelloAct.Naming(what);
        try { (_, said) = await AccountRemoval.RemoveAsync(App.Words, path, what, signing.Sign, signing.Delete, signing.Current); }
        finally { removing = false; }
        await LoadAsync();
        if (said is not null) (why.Text, why.Visibility) = (said, Visibility.Visible);
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Account;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>How Devices reads KIFF as the session; none for a screenshot, which shows the list empty.</summary>
public sealed record DevicesOpening(Func<string, Task<CloudRead>>? Read);

/// <summary>
/// Account › Devices, as the iPad's <c>DevicesView</c>: each device signed in as this person, its name and when it was last
/// used, "This device" for this PC, and the footer. Signing another device out is an approval, signed: until this PC can
/// sign one, Sign out is left out, with Android's line that says to use a device that can approve. A phone adds a computer here; on Windows, this PC is the computer, so that's not here.
/// </summary>
public sealed partial class DevicesPage : Page
{
    public DevicesPage() => InitializeComponent();

    static Style Look(string key) => (Style)Application.Current.Resources[key];

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
        if ((e.Parameter as DevicesOpening)?.Read is not { } read)
        {
            Spinner.IsActive = false;
            Spinner.Visibility = Visibility.Collapsed;
            return;
        }
        var answer = await read(Devices.Path);
        Spinner.IsActive = false;
        Spinner.Visibility = Visibility.Collapsed;
        if (answer.Answer is not { } json || Devices.Read(json) is not { } devices)
        {
            // KIFF's refusal in the iPad's plain words (KIFFError, PlainError), never KIFF's own reason.
            Error.Text = w.Get("settings.devices.not_loaded", ("why", answer.Answer is null ? Tafs.Core.Kiff.KiffErrors.Words(w, answer.Status) : w.Get("error.ours")));
            Error.Visibility = Visibility.Visible;
            return;
        }
        Show(devices);
    }

    /// <summary>One row a device, a line between them: its name and when it was last used; This device on its own; the line under the list for signing another out.</summary>
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
            // This device on its own row; Sign out for another is left out until this PC signs it, with the line below that says why.
            if (device.ThisDevice)
            {
                var right = new TextBlock { Text = w.Get("settings.devices.this_device_title"), FontSize = 13, Style = Look("TafsDimText"), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(right, 1);
                row.Children.Add(right);
            }
            AutomationProperties.SetName(row, $"{name.Text}. {used.Text}" + (device.ThisDevice ? $". {w.Get("settings.devices.this_device_title")}" : ""));
            Rows.Children.Add(new Border { Style = Look("TafsHourLine"), Child = row, Padding = new Thickness(16, 0, 16, 0),
                BorderThickness = new Thickness(0, i > 0 ? 1 : 0, 0, 0) });
        }
        Panel.Visibility = devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Signing another device out is an approval this PC can't sign yet (the iPad's prepareRemoveDevice): Android's line for it.
        NeedsApprover.Text = w.Get("settings.devices.signout_needs_approver");
        NeedsApprover.Visibility = devices.Any(d => !d.ThisDevice) ? Visibility.Visible : Visibility.Collapsed;
    }
}

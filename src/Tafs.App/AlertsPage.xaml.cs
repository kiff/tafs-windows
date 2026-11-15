using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.NeedsYou;

namespace Tafs.App;

/// <summary>What Alerts opens on: the signed-in account's tenant (none for the sample or signed out), and the choices as kept.</summary>
public sealed record AlertsOpening(string? Tenant);

/// <summary>
/// Account › Alerts, as the iPad's <c>NotificationSettingsView</c>, for this PC's own alerts (the notifications a held call
/// raises while tafs runs): what an alert looks like; then, for a KIFF account, "Alerts on this Windows PC", on only when the
/// person turned it on for this account and Windows allows tafs's notifications; and Sound, off and greyed while alerts are
/// off, its choice kept for when they're back.
/// </summary>
public sealed partial class AlertsPage : Page
{
    string? tenant;
    bool drawing;

    public AlertsPage() => InitializeComponent();

    static Style Look(string key) => (Style)Application.Current.Resources[key];

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        tenant = (e.Parameter as AlertsOpening)?.Tenant;
        Heading.Text = w.Get("settings.alerts");
        Previews.Children.Clear();
        foreach (var preview in AlertsScreen.Previews(w)) Previews.Children.Add(Preview(preview));
        OnSwitch.Header = AlertsScreen.OnThisDevice(w);
        SoundTitle.Text = w.Get("alert.sound");
        SoundWords.Text = w.Get("alert.sound_words");
        BlockedText.Text = w.Get("alert.allow_notifications");
        OpenSettings.Content = w.Get("settings.title");
        Choices.Visibility = tenant is null ? Visibility.Collapsed : Visibility.Visible;
        Show();
    }

    /// <summary>On only when chosen for this account and Windows allows it; Sound as chosen while on, off and greyed while off.</summary>
    void Show()
    {
        if (tenant is not { } t) return;
        var prefs = Alerts.Prefs;
        var on = prefs.IsOn(t) && Toasts.Allowed;
        drawing = true;
        OnSwitch.IsOn = on;
        SoundSwitch.IsOn = on && prefs.Sound;
        SoundSwitch.IsEnabled = on;
        drawing = false;
        AutomationProperties.SetName(SoundSwitch, $"{SoundTitle.Text}. {SoundWords.Text}");
    }

    /// <summary>
    /// Turning alerts on asks Windows first, as the iPad asks the system's permission: refused, it says where to allow it
    /// and stays off. Off is kept for this account.
    /// </summary>
    void OnToggled(object sender, RoutedEventArgs e)
    {
        if (drawing || tenant is not { } t) return;
        var wanted = OnSwitch.IsOn;
        Blocked.Visibility = Visibility.Collapsed;
        if (wanted && !Toasts.Allowed)
        {
            Blocked.Visibility = Visibility.Visible;
            Show();
            return;
        }
        Alerts.Save(Alerts.Prefs.With(t, wanted));
        Show();
    }

    void OnSound(object sender, RoutedEventArgs e)
    {
        if (drawing || !SoundSwitch.IsEnabled) return;
        Alerts.Save(Alerts.Prefs with { Sound = SoundSwitch.IsOn });
    }

    void OnOpenSettings(object sender, RoutedEventArgs e) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:notifications"));

    /// <summary>An alert as Windows shows one (the iPad's <c>LockScreenAlert</c>): the mascot, its title and line, and the sound mark.</summary>
    static Border Preview(AlertPreview preview)
    {
        var w = App.Words;
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mascot = new Image { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Top,
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/mascot.png")) };
        var words = new StackPanel { Spacing = 2 };
        words.Children.Add(new TextBlock { Text = preview.Title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, Style = Look("TafsInkText") });
        words.Children.Add(new TextBlock { Text = preview.Line, FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
            Style = Look("TafsSoftText") });
        var mark = new FontIcon { Glyph = ((char)(preview.Loud ? 0xE767 : 0xE7ED)).ToString(), FontSize = 12, VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(words, 1);
        Grid.SetColumn(mark, 2);
        grid.Children.Add(mascot);
        grid.Children.Add(words);
        grid.Children.Add(mark);
        var card = new Border { Child = grid, Padding = new Thickness(12), CornerRadius = new CornerRadius(18) };
        card.SetValue(Border.BackgroundProperty, Application.Current.Resources["TafsRaised"]);
        AutomationProperties.SetName(card, preview.Spoken(w));
        return card;
    }
}

/// <summary>The alert choices, kept beside the session in <c>%LOCALAPPDATA%\tafs\alerts.json</c>.</summary>
static class Alerts
{
    static readonly string File = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tafs", "alerts.json");

    static AlertPrefs? prefs;

    public static AlertPrefs Prefs => prefs ??= AlertPrefs.Read(System.IO.File.Exists(File) ? System.IO.File.ReadAllBytes(File) : null);

    /// <summary>Kept for next time; one that can't be written still holds now.</summary>
    public static void Save(AlertPrefs chosen)
    {
        prefs = chosen;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllBytes(File, chosen.Write());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

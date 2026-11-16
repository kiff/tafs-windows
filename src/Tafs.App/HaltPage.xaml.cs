using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// What the Halt screen opens on: a taf by its tafs cloud id and name, or every taf with none; how it reads and sends a
/// halt as the session; and what reads the list again once a halt is sent, as the iPad's <c>refresh()</c>.
/// </summary>
/// <param name="Resume">Resume behind one Windows Hello (<see cref="HaltResume.ResumeAsync"/>), for a PC that resumes; none
/// for any other, which reads why it can't and approves on another device.</param>
public sealed record HaltOpening(string? Taf, string Name, Func<string, Task<CloudRead>> Read, Func<string, Task<CloudRead>> Send, Action Halted,
    Func<Task<(Halt? Halt, string? Why)>>? Resume = null);

/// <summary>
/// A halt of one taf or, with none, of every taf, as the iPad's <c>HaltScreen</c>. Halting is one click with no Windows
/// Hello: it only takes authority away. It reads Halted only once kiff/agents, KIFF and planque all hold it, Halting… until
/// then, and is read again every 2 seconds while halting, for up to 2 minutes. Resuming gives authority back, so it's
/// signed by this PC's passkey behind Windows Hello ("Resume with Windows Hello"); a PC that can't sign it says to approve
/// on another device.
/// </summary>
public sealed partial class HaltPage : Page
{
    HaltOpening? opening;
    Halt halt = Halt.Running;
    bool busy;

    /// <summary>Each opening moves it on: a read for a screen left behind is dropped.</summary>
    int shown;

    public HaltPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        opening = e.Parameter as HaltOpening;
        if (opening is null) return;
        var w = App.Words;
        Heading.Text = w.Get(opening.Taf is null ? "taf.halt.title_all" : "taf.halt.title");
        TafName.Text = opening.Taf is null ? w.Get("taf.halt.all_tafs") : opening.Name;
        HaltButton.Content = w.Get(opening.Taf is null ? "halt.all_now" : "halt.now");
        AgainButton.Content = w.Get("halt.again");
        WaitingText.Text = w.Get("taf.halt.halting_wait");
        ResumeElsewhere.Text = w.Get("approval.device_can_not_approve_approve");
        ResumeButton.Content = w.Get("taf.halt.resume_with", ("biometry", Tafs.Core.Coach.CoachScreen.Biometry));
        Show();
        var at = ++shown;
        await RefreshAsync(at);
        await FollowAsync(at);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => shown++;

    /// <summary>The halt as tafs cloud has it now (the iPad's <c>refreshHalt</c>); a read that fails leaves it as it was.</summary>
    async Task RefreshAsync(int at)
    {
        if (opening is null) return;
        var read = await opening.Read(Halt.Path(opening.Taf));
        if (at != shown || read.Answer is not { } answer || Halt.Read(answer) is not { } h) return;
        halt = h;
        Show();
    }

    /// <summary>Read again every 2 seconds while it's halting, for up to 2 minutes.</summary>
    async Task FollowAsync(int at)
    {
        for (var i = 0; i < 60 && at == shown && halt.State == HaltState.Halting; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            await RefreshAsync(at);
        }
    }

    /// <summary>
    /// Halt now, Halt all now, or Halt again, which asks a part that didn't take it again (the iPad's <c>halt</c>): the list
    /// is read again at once, so it reads halted there too, then the halt is followed.
    /// </summary>
    async void OnHalt(object sender, RoutedEventArgs e)
    {
        if (opening is null || busy) return;
        var w = App.Words;
        var at = shown;
        busy = true;
        Error.Visibility = Visibility.Collapsed;
        Show();
        var sent = await opening.Send(Halt.Path(opening.Taf));
        if (at != shown) return;
        if (sent.Answer is { } answer && Halt.Read(answer) is { } h)
        {
            halt = h;
            opening.Halted();
        }
        else
        {
            Error.Text = w.Get(opening.Taf is null ? "taf.halt.not_halted_all" : "taf.halt.not_halted", ("why", sent.Why ?? w.Get("error.ours")));
            Error.Visibility = Visibility.Visible;
        }
        busy = false;
        Show();
        await FollowAsync(at);
    }

    /// <summary>
    /// Resume, as the iPad's: one Windows Hello, then KIFF, then tafs cloud. Taken, it reads Running, and the list and the
    /// sidebar are read again; not, why shows in red, and the halt stays.
    /// </summary>
    async void OnResume(object sender, RoutedEventArgs e)
    {
        if (opening?.Resume is not { } resume || busy) return;
        var at = shown;
        busy = true;
        Error.Visibility = Visibility.Collapsed;
        Show();
        var (h, why) = await resume();
        if (at != shown) return;
        busy = false;
        if (h is not null)
        {
            halt = h;
            opening.Halted();
        }
        else if (why is not null)
        {
            Error.Text = why;
            Error.Visibility = Visibility.Visible;
        }
        Show();
    }

    void Show()
    {
        if (opening is null) return;
        var w = App.Words;
        var running = halt.State == HaltState.Running;
        TafName.Foreground = Brush(running ? "TafsDim" : "TafsNo");
        Headline.Text = Halt.Headline(w, halt);
        Explanation.Text = Halt.Explanation(w, halt, opening.Taf is null, TimeZoneInfo.Local);

        Holds.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        HoldRows.Children.Clear();
        HoldRows.Children.Add(new TextBlock { Text = w.Get("taf.halt.who_holds"), Style = (Style)Application.Current.Resources["TafsEyebrow"] });
        HoldRows.Children.Add(HoldRow(w.Get("taf.halt.no_run_starts"), halt.Runtime));
        HoldRows.Children.Add(HoldRow(w.Get("taf.halt.kiff_refuses"), halt.Kiff));
        HoldRows.Children.Add(HoldRow(w.Get("taf.halt.connection_sends_nothing"), halt.Planque));
        if (halt.Account && opening.Taf is not null)
            HoldRows.Children.Add(new TextBlock { Text = w.Get("taf.halt.halted_with_all"), FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["TafsDimText"] });

        HaltButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        HaltButton.IsEnabled = !busy;
        if (busy && running) HaltButton.Content = w.Get("taf.halt.halting");
        else HaltButton.Content = w.Get(opening.Taf is null ? "halt.all_now" : "halt.now");
        Waiting.Visibility = halt.State == HaltState.Halting ? Visibility.Visible : Visibility.Collapsed;
        AgainButton.Visibility = Waiting.Visibility;
        AgainButton.IsEnabled = !busy;
        // A taf halted with every taf is resumed from every taf's screen, as the iPad's.
        var resumable = halt.State == HaltState.Halted && !(halt.Account && opening.Taf is not null);
        ResumeButton.Visibility = resumable && opening.Resume is not null ? Visibility.Visible : Visibility.Collapsed;
        ResumeButton.IsEnabled = !busy;
        if (busy && resumable) ResumeButton.Content = w.Get("taf.halt.resuming");
        else ResumeButton.Content = w.Get("taf.halt.resume_with", ("biometry", Tafs.Core.Coach.CoachScreen.Biometry));
        ResumeElsewhere.Visibility = resumable && opening.Resume is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One part of the halt (the iPad's <c>HoldRow</c>): a check once it holds it, its words, and Done or Waiting.</summary>
    Grid HoldRow(string text, bool held)
    {
        var w = App.Words;
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = new FontIcon { Glyph = ((char)(held ? 0xE73E : 0xEA3A)).ToString(), FontSize = 14,
            Foreground = Brush(held ? "TafsOk" : "TafsDim") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(mark, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var words = new TextBlock { Text = text, FontSize = 15, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["TafsInkText"] };
        var state = new TextBlock { Text = w.Get(held ? "taf.halt.part_held" : "taf.halt.part_waiting"), FontSize = 13,
            Style = (Style)Application.Current.Resources["TafsDimText"] };
        Grid.SetColumn(words, 1);
        Grid.SetColumn(state, 2);
        row.Children.Add(mark);
        row.Children.Add(words);
        row.Children.Add(state);
        return row;
    }

    /// <summary>A theme brush for the window's theme, light or dark, as the Coach panel finds one.</summary>
    Microsoft.UI.Xaml.Media.Brush Brush(string key)
    {
        var theme = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var merged in Application.Current.Resources.MergedDictionaries.Prepend(Application.Current.Resources))
            if (merged.ThemeDictionaries.TryGetValue(theme, out var found) && found is ResourceDictionary d && d.TryGetValue(key, out var brush)
                && brush is Microsoft.UI.Xaml.Media.Brush b)
                return b;
        return (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[key];
    }
}

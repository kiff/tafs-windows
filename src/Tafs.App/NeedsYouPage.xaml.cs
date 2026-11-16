using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.NeedsYou;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>One held call on its card. Its countdown and its footer change while it's open, every second.</summary>
public sealed class HeldCallView : INotifyPropertyChanged
{
    readonly WordBook words;

    public HeldCallView(WordBook words, HeldCall call, DateTimeOffset now, bool mayAnswerHolds = true, bool managesCards = true)
    {
        this.words = words;
        Call = call;
        lines = NeedsYouScreen.Card(words, call, now);
        var rgb = TafColors.Of(call.TafColor);
        Avatar = new SolidColorBrush(ColorHelper.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        var gate = NeedsYouScreen.Gate(words);
        (GateLead, GateAdd) = (gate.Lead, gate.Add);
        // The gate only while this PC can't approve: a view-only session, as the iPad's ApprovalGate.
        var answers = NeedsYouScreen.Answers(words, call, mayAnswerHolds, managesCards, App.Sessions.Kept()?.Mode);
        (AnswerLine, GateShown) = (answers.Line, answers.Gate ? Visibility.Visible : Visibility.Collapsed);
        AvatarShown = call.Avatar ? Visibility.Visible : Visibility.Collapsed;
        DetailsHeading = words.Get("common.details");
        DetailsText = string.Join(Environment.NewLine, call.Details);
    }

    /// <summary>What it's on, one line an argument, under Details; empty for a call KIFF kept no arguments for.</summary>
    public string DetailsHeading { get; }
    public string DetailsText { get; }

    public HeldCall Call { get; }
    public Brush Avatar { get; }
    public string GateLead { get; }
    public string GateAdd { get; }

    /// <summary>Where the answers go, as the iPad's: who answers, or the line over a Card total; the gate only where ApprovalGate would be.</summary>
    public string? AnswerLine { get; }
    public Visibility GateShown { get; }

    /// <summary>A taf's and Coach's calls have an avatar; an unknown agent's has none.</summary>
    public Visibility AvatarShown { get; }

    HeldCallLines lines;
    public HeldCallLines Lines => lines;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Counts down: "9:37 left", then "expired", when the answers give way to the expired line.</summary>
    public void Tick(DateTimeOffset now)
    {
        var next = NeedsYouScreen.Card(words, Call, now);
        if (next == lines) return;
        lines = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lines)));
    }

    public static Visibility Seen(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Needs you: KIFF's held calls, or the iPad's samples for a screenshot.</summary>
public sealed partial class NeedsYouPage : Page
{
    /// <summary>Add this Windows PC, from a card's answers.</summary>
    public static event Action? AddThisDeviceChosen;

    /// <summary>Done (true) or Skip on a step.</summary>
    public static event Action<YourStep, bool>? StepAnswerChosen;

    /// <summary>F5: Needs you read again, the Windows form of the iPad's pull-to-refresh.</summary>
    public static event Action? RefreshChosen;

    /// <summary>KIFF's <c>holds.answer</c> and <c>cards.manage</c> for the person, from <c>GET /v1/me</c>; both allowed until read.</summary>
    internal static (bool MayAnswerHolds, bool ManagesCards) Capabilities { get; set; } = (true, true);

    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    IReadOnlyList<HeldCallView> views = [];
    IReadOnlyList<Unproven> unproven = [];
    IReadOnlyList<YourStep> steps = [];

    public NeedsYouPage()
    {
        InitializeComponent();
        var refresh = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.F5 };
        refresh.Invoked += (_, e) => { e.Handled = true; RefreshChosen?.Invoke(); };
        KeyboardAccelerators.Add(refresh);
        clock.Tick += (_, _) => { foreach (var view in views) view.Tick(DateTimeOffset.UtcNow); };
        Unloaded += (_, _) => clock.Stop();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        Heading.Text = NeedsYouScreen.Title(w);
        (EmptyLead.Text, EmptyHint.Text) = NeedsYouScreen.Empty(w);
        Show(e.Parameter as IReadOnlyList<HeldCall> ?? []);
    }

    /// <summary>The held calls, each on its card, or that nothing waits: drawn again with each read of KIFF's.</summary>
    internal void Show(IReadOnlyList<HeldCall> calls)
    {
        var w = App.Words;
        var now = DateTimeOffset.UtcNow;
        views = calls.Select(c => new HeldCallView(w, c, now, Capabilities.MayAnswerHolds, Capabilities.ManagesCards)).ToList();
        Calls.ItemsSource = views;
        ShowEmpty();
        if (views.Count > 0) clock.Start();
        else clock.Stop();
    }

    void OnAddThisDevice(object sender, RoutedEventArgs e) => AddThisDeviceChosen?.Invoke();

    /// <summary>Nothing waiting only with no held call and no call to say about, as the iPad's.</summary>
    void ShowEmpty() => Empty.Visibility = views.Count == 0 && unproven.Count == 0 && steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Why the last answer didn't go through, at the top in red, as the iPad's <c>lastError</c>; none hides it.</summary>
    internal void ShowError(string? why)
    {
        Error.Text = why ?? "";
        Error.Visibility = why is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Your step, each on its card, as the iPad's <c>YourStepCard</c>, every word tafs cloud's: its title as the eyebrow; the
    /// taf's name, 14 semibold; what, 16 semibold; why, 14 soft; the vendor's page as an outlined button only when tafs cloud
    /// vouched for its link, with its host under it, 12 mono dim; Done and Skip only when this PC can answer, off while
    /// answering.
    /// </summary>
    internal void ShowSteps(IReadOnlyList<YourStep> listed, bool canAnswer, IReadOnlySet<string> answering)
    {
        steps = listed;
        Steps.Children.Clear();
        foreach (var step in listed)
        {
            var lines = new StackPanel { Spacing = 10 };
            if (step.Said.Title.Length > 0) lines.Children.Add(new TextBlock { Text = step.Said.Title, Style = (Style)Application.Current.Resources["TafsEyebrow"] });
            if (step.TafName is { Length: > 0 } name) lines.Children.Add(Line(name, 14, "TafsInkText", semibold: true));
            lines.Children.Add(Line(step.What, 16, "TafsInkText", semibold: true));
            if (step.Why.Length > 0) lines.Children.Add(Line(step.Why, 14, "TafsSoftText"));
            if (YourSteps.OpenLink(step) is { } link)
            {
                var open = new Button
                {
                    Style = (Style)Application.Current.Resources["TafsOutlineButton"],
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 6,
                        Children = { new FontIcon { Glyph = "\uE8A7", FontSize = 14 }, new TextBlock { Text = link.Words } },
                    },
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, link.Words);
                open.Click += (_, _) => _ = Windows.System.Launcher.LaunchUriAsync(link.Url);
                // Where it really goes, as tafs cloud read the link.
                var host = new TextBlock { Text = link.Host, FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Style = (Style)Application.Current.Resources["TafsDimText"] };
                lines.Children.Add(new StackPanel { Spacing = 4, Children = { open, host } });
            }
            if (canAnswer)
            {
                var busy = answering.Contains(step.Handoff);
                // The iPad's PrimaryButtonStyle, and its look while off, as Coach's Send answers.
                var done = new Button { Content = step.Said.Done, Style = (Style)Application.Current.Resources[busy ? "TafsPrimaryOff" : "TafsPrimary"], IsEnabled = !busy };
                var skip = new Button { Content = step.Said.Skip, Style = (Style)Application.Current.Resources["TafsOutlineButton"], IsEnabled = !busy };
                done.Click += (_, _) => StepAnswerChosen?.Invoke(step, true);
                skip.Click += (_, _) => StepAnswerChosen?.Invoke(step, false);
                lines.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { done, skip } });
            }
            var card = new Border
            {
                Background = ThemeBrush("TafsPanel"), BorderBrush = (Brush)Application.Current.Resources["TafsAmberEdge"],
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(16), Child = lines,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(card, "yourStep." + step.Handoff);
            Steps.Children.Add(card);
        }
        ShowEmpty();
    }

    /// <summary>
    /// "Did this happen?", each call on its card, as the iPad's <c>DidThisHappenCard</c>: on the panel with the amber edge, its
    /// title in amber, 13 semibold; KIFF's words for the call, else its title, 15 semibold; the amount only without KIFF's
    /// words; why KIFF asks, 13 soft. For someone who answers held calls, the answers: until this PC approves, why it can't
    /// and Add this Windows PC, as on a held call.
    /// </summary>
    internal void ShowUnproven(IReadOnlyList<Unproven> calls, bool mayAnswerHolds)
    {
        var w = App.Words;
        unproven = calls;
        UnprovenCalls.Children.Clear();
        var gate = NeedsYouScreen.Gate(w);
        foreach (var call in calls)
        {
            var (title, said, amount, why) = DidThisHappen.Card(w, call);
            var lines = new StackPanel { Spacing = 10 };
            lines.Children.Add(Line(title, 13, "TafsAmberWords", semibold: true));
            lines.Children.Add(Line(said, 15, "TafsInkText", semibold: true));
            if (amount is not null) lines.Children.Add(Line(amount, 15, "TafsInkText"));
            if (why is not null) lines.Children.Add(Line(why, 13, "TafsSoftText"));
            if (DidThisHappen.Gate(mayAnswerHolds, App.Sessions.Kept()?.Mode ?? Tafs.Core.Kiff.ApprovalMode.View))
            {
                var add = new Button { Content = gate.Add, Style = (Style)Application.Current.Resources["TafsOutlineButton"] };
                add.Click += OnAddThisDevice;
                lines.Children.Add(new StackPanel { Spacing = 8, Children = { Line(gate.Lead, 13, "TafsSoftText"), add } });
            }
            var card = new Border
            {
                Background = ThemeBrush("TafsPanel"), BorderBrush = (Brush)Application.Current.Resources["TafsAmberEdge"],
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(16), Child = lines,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(card, "happened." + call.Proposal);
            UnprovenCalls.Children.Add(card);
        }
        ShowEmpty();
    }

    /// <summary>A theme brush in this page's theme, as <see cref="CoachPanel"/> finds one: Theme.xaml's dictionaries first.</summary>
    Brush ThemeBrush(string key)
    {
        var theme = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var merged in Application.Current.Resources.MergedDictionaries.Prepend(Application.Current.Resources))
            if (merged.ThemeDictionaries.TryGetValue(theme, out var found) && found is ResourceDictionary d && d.TryGetValue(key, out var brush)
                && brush is Brush b)
                return b;
        return (Brush)Application.Current.Resources[key];
    }

    static TextBlock Line(string text, double size, string style, bool semibold = false) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources[style],
        FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
    };
}

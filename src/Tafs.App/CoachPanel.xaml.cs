using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Windows.UI.ViewManagement;

namespace Tafs.App;

/// <summary>Coach mode's panel. Its conversation (tafs cloud's Setup) comes in its own change: until then, the intro.</summary>
public sealed partial class CoachPanel : UserControl
{
    /// <summary>The iPad's shadow reaches 24 past the panel's edge: a slide goes that much further, so none of it stays.</summary>
    const double ShadowReach = 24;

    /// <summary>After it has gone: the window gives focus back to what opened it.</summary>
    public event Action? Closed;

    /// <summary>How the panel reads tafs cloud, as the session; none for the sample. Given while it's open, it reads at once.</summary>
    public Func<string, Task<CloudRead>>? Read
    {
        get => read;
        set
        {
            read = value;
            if (IsOpen) _ = LoadThreadAsync();
        }
    }

    Func<string, Task<CloudRead>>? read;

    /// <summary>How the panel sends a card's answer to tafs cloud (path and body), as the session; none for a screenshot.</summary>
    public Func<string, byte[], Task<CloudRead>>? Answer { get; set; }

    /// <summary>Coach's id, its thread as last read, and each card's state here, as the iPad's model keeps them.</summary>
    string? coach;
    CoachThread? thread;
    readonly HashSet<string> answered = [];
    readonly Dictionary<string, string> notes = [];
    readonly Dictionary<string, QuestionsCardState> cards = [];

    public CoachPanel()
    {
        InitializeComponent();
        var w = App.Words;
        var intro = CoachScreen.Intro(w);
        TitleText.Text = intro.Title;
        Heading.Text = intro.Heading;
        LineText.Text = intro.Line;
        Start.Content = intro.Start;
        Footnote.Text = intro.Footnote;
        AutomationProperties.SetName(Back, w.Get("common.back"));
        AutomationProperties.SetName(FullScreen, w.Get("coach.full_screen"));
        AutomationProperties.SetName(Backdrop, w.Get("coach.close_coach_mode"));
        KeyboardAccelerators.Add(Escape());
    }

    static CoachMotion Motion => CoachScreen.Motion(new UISettings().AnimationsEnabled);

    bool IsOpen => Visibility == Visibility.Visible;

    /// <summary>While it's sliding out: still there, but going.</summary>
    bool closing;

    /// <summary>It slides in from the right (fades, with Windows' animations off), and focus moves to Back.</summary>
    /// <param name="animate">False to be there at once, as for a screenshot.</param>
    public void Open(bool animate = true)
    {
        if (closing)
        {
            // Opened again while it's going: it comes back.
            closing = false;
            Animate(0, 1, 1);
            return;
        }
        if (IsOpen) return;
        Fit(WindowWidth);
        var slide = animate && Motion == CoachMotion.Slide;
        var fade = animate && Motion == CoachMotion.Fade;
        Hold(slide ? Off : 0, fade ? 0 : 1, animate ? 0 : 1);
        Visibility = Visibility.Visible;
        if (animate) Animate(0, 1, 1);
        DispatcherQueue.TryEnqueue(() => Back.Focus(FocusState.Programmatic));
        _ = LoadThreadAsync();
    }

    /// <summary>
    /// Coach's newest conversation, read as the panel opens, as the iPad's <c>loadSetupThread</c>. With messages, the thread
    /// shows in place of the intro; with no Coach yet, none, or a read that fails, the intro stays.
    /// </summary>
    async Task LoadThreadAsync()
    {
        if (read is not { } reader) return;
        var (loaded, error, id) = await CoachChat.LoadAsync(App.Words, reader);
        IntroError.Text = error ?? "";
        IntroError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        coach = id;
        if (loaded is null || loaded.Drawn(Kinds).Count == 0) return;
        thread = loaded;
        ShowThread(loaded);
        Intro.Visibility = Visibility.Collapsed;
        ThreadView.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The thread, as the iPad's <c>SetupChatView.thread</c>: a time between parts an hour apart; the person's words on the
    /// right, in a brand bubble, "Answered the card above" under an answer; Coach's full width, "Worked for 12 s" above them,
    /// which opens its steps; a run's start as a line; then what ended while the person was away, and how handed tasks went.
    /// </summary>
    void ShowThread(CoachThread thread)
    {
        var w = App.Words;
        ThreadItems.Children.Clear();
        var items = thread.Drawn(Kinds);
        for (var i = 0; i < items.Count; i++)
        {
            if (CoachChat.GapTime(w, items, i, DateTimeOffset.Now, TimeZoneInfo.Local) is { } gap)
                ThreadItems.Children.Add(Words(gap, 12, "TafsDimText", HorizontalAlignment.Center));
            // A card, as the iPad's SetupComponentView: the questions card in this change; the other kinds come in their own.
            if (items[i].Card is { } shown)
            {
                if (shown.Card.Kind == "questions") ThreadItems.Children.Add(QuestionsCard(thread, shown));
                continue;
            }
            var m = items[i].Message!;
            // As the iPad's ChatBubble: a run's start is a line, inside either branch, so its work and answered line still show.
            if (m.From == ChatFrom.Owner)
            {
                var said = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(48, 0, 0, 0) };
                var words = CoachChat.OwnerWords(w, m.Text, thread.Shown);
                if (m.Kickoff) said.Children.Add(Kickoff(m));
                else if (words.Length > 0)
                    said.Children.Add(new Border
                    {
                        Background = Brush("TafsBrand"), CornerRadius = new CornerRadius(18),
                        Padding = new Thickness(14, 10, 14, 10), HorizontalAlignment = HorizontalAlignment.Right,
                        Child = new TextBlock { Text = words, FontSize = 15, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
                            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) },
                    });
                if (m.Answers is not null) said.Children.Add(Words(w.Get("chat.answered_card_above"), 12, "TafsDimText", HorizontalAlignment.Right));
                if (said.Children.Count > 0) ThreadItems.Children.Add(said);
            }
            else
            {
                var said = new StackPanel { Spacing = 6 };
                if (m.Work is { } work) said.Children.Add(WorkSummary(work));
                if (m.Kickoff) said.Children.Add(Kickoff(m));
                else
                {
                    var text = Words(m.Text, 16, "TafsInkText");
                    text.IsTextSelectionEnabled = true;
                    said.Children.Add(text);
                }
                ThreadItems.Children.Add(said);
            }
        }
        var away = thread.Lines.Where(l => l.Away).ToList();
        if (away.Count > 0)
        {
            var box = new StackPanel { Spacing = 6 };
            var title = Words(w.Get("coach.away"), 14, "TafsInkText");
            title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            box.Children.Add(title);
            foreach (var line in away) box.Children.Add(Words($"{line.Taf}: {line.Said}", 13, "TafsSoftText"));
            ThreadItems.Children.Add(new Border { Child = box, Padding = new Thickness(12), CornerRadius = new CornerRadius(14),
                Background = Brush("TafsRaised") });
        }
        foreach (var line in thread.Lines.Where(l => !l.Away))
            ThreadItems.Children.Add(Words($"↳ {line.Taf}: {line.Said}", 13, "TafsSoftText"));
    }

    /// <summary>The kinds of card drawn so far: the others come in their own changes, their answers in bubbles until then.</summary>
    static readonly string[] Kinds = ["questions"];

    QuestionsCardView QuestionsCard(CoachThread thread, ShownCard shown)
    {
        if (!cards.TryGetValue(shown.Ref, out var state)) cards[shown.Ref] = state = new QuestionsCardState();
        return new QuestionsCardView(shown, thread.AnswerTo(shown.Card.Id), state, answered.Contains(shown.Ref), notes.GetValueOrDefault(shown.Card.Id),
            text => _ = AnswerAsync(shown, text), Brush);
    }

    /// <summary>
    /// A card's answer, as the iPad's <c>answer(_:_:)</c>: sent as data, the card busy meanwhile. Sent, the thread is read
    /// again (the iPad then follows the run's stream, which comes with writing to Coach); a card that doesn't wait any more
    /// (409) is read again; refused for want of the AI-data consent, the card stays as chosen; else it says it didn't reach.
    /// </summary>
    async Task AnswerAsync(ShownCard shown, string text)
    {
        if (Answer is not { } post || coach is null || CoachChat.AnswerRequest(coach, shown, text) is not { } request) return;
        answered.Add(shown.Ref);
        notes.Remove(shown.Card.Id);
        Redraw();
        var sent = CoachChat.Sent(await post(request.Path, request.Body));
        if (sent != CardSent.Sent) answered.Remove(shown.Ref);
        if (sent == CardSent.NotReached) notes[shown.Card.Id] = App.Words.Get("coach.answer_did_not_reach_setup");
        if (sent is CardSent.Sent or CardSent.ReadAgain) await LoadThreadAsync();
        else Redraw();
    }

    void Redraw()
    {
        if (thread is not null) ShowThread(thread);
    }

    /// <summary>A run's start, as the iPad's kickoff line: "Run started 09:00 · …", centred, two lines at most.</summary>
    static TextBlock Kickoff(ChatMessage m)
    {
        var line = Words(App.Words.Get("chat.run_started_with", ("time", TimeZoneInfo.ConvertTime(m.At, TimeZoneInfo.Local).ToString("HH:mm",
            System.Globalization.CultureInfo.InvariantCulture)), ("text", m.Text)), 12, "TafsDimText", HorizontalAlignment.Center);
        line.MaxLines = 2;
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        return line;
    }

    /// <summary>
    /// A finished answer's work, folded, as the iPad's <c>WorkSummary(coach: true)</c>: "Worked for 12 s ›", which opens its
    /// steps, each with its mark, in Coach's words, "2 tries" after a step tried again.
    /// </summary>
    StackPanel WorkSummary(ChatWork work)
    {
        var w = App.Words;
        var rows = WorkLog.CoachRows(w, work);
        var took = WorkLog.Duration(w, work.Ms);
        var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 11, Foreground = Brush("TafsDim") };
        var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        label.Children.Add(Words(w.Get("chat.work.worked_for", ("ms", took)), 13, "TafsDimText"));
        label.Children.Add(chevron);
        var toggle = new Button { Content = label, Style = (Style)Application.Current.Resources["TafsQuietButton"], Padding = new Thickness(0) };
        AutomationProperties.SetName(toggle, w.Count("chat.work.worked_for_steps", rows.Count, ("took", took)));
        AutomationProperties.SetHelpText(toggle, w.Get("chat.work.shows_steps"));
        var steps = new StackPanel { Spacing = 4, Padding = new Thickness(6, 0, 0, 0), Margin = new Thickness(4, 0, 0, 0), Visibility = Visibility.Collapsed,
            BorderThickness = new Thickness(1, 0, 0, 0), BorderBrush = Brush("TafsLine") };
        foreach (var row in rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            line.Children.Add(new FontIcon { Glyph = row.Status switch { StepStatus.Error => "\uE783", StepStatus.NotMade => "\uE7A7", _ => "\uE73E" }, FontSize = 11, Width = 14,
                Foreground = Brush(row.Status == StepStatus.Error ? "TafsNo" : "TafsDim") });
            line.Children.Add(Words(WorkLog.Past(row.Words), 13, "TafsSoftText"));
            if (row.Status == StepStatus.NotMade) line.Children.Add(Words(w.Get("chat.work.not_made_read_first"), 12, "TafsDimText"));
            else if (row.Tries > 1) line.Children.Add(Words(w.Count("chat.work.tries", row.Tries), 12, "TafsDimText"));
            steps.Children.Add(line);
        }
        toggle.Click += (_, _) =>
        {
            var open = steps.Visibility != Visibility.Visible;
            steps.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            chevron.Rotation = open ? 90 : 0;
            AutomationProperties.SetHelpText(toggle, w.Get(open ? "chat.work.hides_steps" : "chat.work.shows_steps"));
        };
        chevron.CenterPoint = new System.Numerics.Vector3(5.5f, 5.5f, 0);
        var summary = new StackPanel { Spacing = 6 };
        summary.Children.Add(toggle);
        summary.Children.Add(steps);
        return summary;
    }

    /// <summary>
    /// A theme brush in this panel's theme: Theme.xaml's dictionaries are merged into the app's, so they're looked for there;
    /// a brush that's the same in both themes is the app's own.
    /// </summary>
    Microsoft.UI.Xaml.Media.Brush Brush(string key)
    {
        var theme = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var merged in Application.Current.Resources.MergedDictionaries.Prepend(Application.Current.Resources))
            if (merged.ThemeDictionaries.TryGetValue(theme, out var found) && found is ResourceDictionary d && d.TryGetValue(key, out var brush)
                && brush is Microsoft.UI.Xaml.Media.Brush b)
                return b;
        return (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[key];
    }

    internal static TextBlock Words(string text, double size, string style, HorizontalAlignment align = HorizontalAlignment.Left) =>
        new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = align,
            TextAlignment = align == HorizontalAlignment.Center ? TextAlignment.Center : TextAlignment.Left,
            Style = (Style)Application.Current.Resources[style] };

    /// <summary>It slides back out to the right (or fades), then tells the window it has gone.</summary>
    public void Close()
    {
        if (!IsOpen || closing) return;
        closing = true;
        Animate(Motion == CoachMotion.Slide ? Off : 0, Motion == CoachMotion.Fade ? 0 : 1, 0, () =>
        {
            closing = false;
            Visibility = Visibility.Collapsed;
            Hold(0, 1, 1);
            Closed?.Invoke();
        });
    }

    void OnBack(object sender, RoutedEventArgs e) => Close();

    bool full;

    /// <summary>⤢ makes it full screen, and brings it back to a third: its edge moves, as the iPad's does.</summary>
    void OnFull(object sender, RoutedEventArgs e)
    {
        var before = PanelColumn.Width.Value;
        full = !full;
        FullIcon.Glyph = full ? "" : "";
        AutomationProperties.SetName(FullScreen, App.Words.Get(full ? "coach.back_width" : "coach.full_screen"));
        var after = WidthIn(Over.ActualWidth);
        if (Motion == CoachMotion.Fade)
        {
            Fit(Over.ActualWidth);
            return;
        }
        if (full)
        {
            // Wide at once, its edge where it was, then the edge moves to the left.
            Fit(Over.ActualWidth);
            Hold(after - before, 1, 1);
            Animate(0, 1, 1);
        }
        else
        {
            // The edge moves to the right, then it's a third wide.
            Animate(before - after, 1, 1, () =>
            {
                Fit(Over.ActualWidth);
                Hold(0, 1, 1);
            });
        }
    }

    void OnBackdrop(object sender, TappedRoutedEventArgs e) => Close();

    void OnSized(object sender, SizeChangedEventArgs e)
    {
        if (running is null) Fit(e.NewSize.Width);
    }

    double WidthIn(double window) => full ? window : CoachScreen.PanelWidth(window);

    void Fit(double window) => PanelColumn.Width = new GridLength(WidthIn(window));

    double WindowWidth => (Parent as FrameworkElement)?.ActualWidth ?? Over.ActualWidth;

    /// <summary>Off past the right edge: the panel's width and its shadow.</summary>
    double Off => PanelColumn.Width.Value + ShadowReach;

    Storyboard? running;

    /// <summary>Where it rests: how far to the right, how opaque the panel is, and the backdrop.</summary>
    void Hold(double slide, double panel, double backdrop)
    {
        Slide.X = slide;
        Panel.Opacity = panel;
        Backdrop.Opacity = backdrop;
    }

    /// <summary>
    /// Moves to where it rests next, easing out over 0.22 s. A newer move takes over: the one before stops where it
    /// rested, so nothing it held stays behind.
    /// </summary>
    void Animate(double slide, double panel, double backdrop, Action? then = null)
    {
        running?.Stop();
        var sb = new Storyboard();
        To(sb, Slide, "X", slide);
        To(sb, Panel, "Opacity", panel);
        To(sb, Backdrop, "Opacity", backdrop);
        sb.Completed += (_, _) =>
        {
            if (running != sb) return;
            running = null;
            Hold(slide, panel, backdrop);
            sb.Stop();
            then?.Invoke();
        };
        running = sb;
        sb.Begin();
    }

    static void To(Storyboard sb, DependencyObject target, string property, double to)
    {
        var a = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(CoachScreen.MotionTime),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, property);
        sb.Children.Add(a);
    }

    /// <summary>Escape closes it, as Close does.</summary>
    KeyboardAccelerator Escape()
    {
        var k = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        k.Invoked += (_, a) => { Close(); a.Handled = true; };
        return k;
    }
}

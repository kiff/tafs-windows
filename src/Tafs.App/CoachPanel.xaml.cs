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

    /// <summary>
    /// How the panel sends a card's answer to tafs cloud (path and body), as the session: only for someone who may use Coach;
    /// none for a screenshot. Without it, a card's Send stays off. Given while it's open, the thread is drawn again.
    /// </summary>
    public Func<string, byte[], Task<CloudRead>>? Answer
    {
        get => answer;
        set
        {
            answer = value;
            Redraw();
        }
    }

    Func<string, byte[], Task<CloudRead>>? answer;

    /// <summary>
    /// Start, as the iPad's <c>startSetup</c>, for a session that can approve; none otherwise, and Start stays off, as the
    /// iPad's behind its approval gate.
    /// </summary>
    public Func<Task<StartResult>>? StartCoach
    {
        get => startCoach;
        set
        {
            startCoach = value;
            firstStart = false;
            ShowStart();
        }
    }

    Func<Task<StartResult>>? startCoach;

    /// <summary>
    /// Signed in, but this PC can't approve (a view session, a key session, or a passkey session that doesn't know its
    /// passkey): the iPad's gate line, <c>approval.device_can_not_approve_approve</c>, where Start would be (its
    /// <c>ApprovalGate</c> in <c>introBefore</c>).
    /// </summary>
    public bool StartGate
    {
        get => startGate;
        set
        {
            startGate = value;
            ShowStart();
        }
    }

    bool startGate;

    /// <summary>While Start runs, as the iPad's <c>startingSetup</c>: Start says "Starting…" and is off.</summary>
    bool starting;

    /// <summary>tafs cloud has its own first start for this account, which isn't on this PC yet: Start stays off.</summary>
    bool firstStart;

    void ShowStart()
    {
        var on = startCoach is not null && !starting && !firstStart;
        Start.Visibility = startGate ? Visibility.Collapsed : Visibility.Visible;
        StartGateLine.Visibility = startGate ? Visibility.Visible : Visibility.Collapsed;
        StartGateLine.Text = App.Words.Get("approval.device_can_not_approve_approve");
        Start.IsEnabled = on;
        Start.Style = (Style)Application.Current.Resources[on ? "TafsPrimary" : "TafsPrimaryOff"];
        Start.Content = App.Words.Get(starting ? "common.starting" : "coach.start");
    }

    /// <summary>
    /// Start, as the iPad's button: <c>startSetup</c> once, then Coach's thread read again, as its <c>loadSetupThread</c>.
    /// Why it didn't start shows under the intro, as the iPad's <c>setupError</c>; closing Windows Hello says nothing.
    /// </summary>
    async void OnStart(object sender, RoutedEventArgs e)
    {
        if (startCoach is not { } start || starting) return;
        var gen = generation;
        starting = true;
        IntroError.Visibility = Visibility.Collapsed;
        ShowStart();
        var result = await start();
        if (gen != generation) return;
        starting = false;
        firstStart = result.Outcome == StartOutcome.FirstStart;
        ShowStart();
        if (result.Started) await LoadThreadAsync();
        else if (result.Why is { } why)
        {
            IntroError.Text = why;
            IntroError.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// How the panel reads planque's grants, signed as this device, and a KIFF draft by its id, for the approval cards: the
    /// session's; none signed out or for a screenshot, and the cards then say they're reading the request.
    /// </summary>
    public Func<Task<IReadOnlyList<Tafs.Core.Kiff.PlanqueGrant>?>>? Grants { get; set; }
    public Func<string, Task<Tafs.Core.Approvals.AgentDraft?>>? Drafts { get; set; }

    /// <summary>The grants and drafts the thread's approvals name, as last read (the iPad's <c>grants</c> and <c>setupDrafts</c>).</summary>
    IReadOnlyList<Tafs.Core.Kiff.PlanqueGrant>? grants;
    readonly Dictionary<string, Tafs.Core.Approvals.AgentDraft> drafts = [];

    /// <summary>Which account the panel's state is for: each sign-out moves it on, so an answer or read in flight is dropped, as the iPad's <c>accountGeneration</c>.</summary>
    int generation;

    /// <summary>Signed out: the thread, Coach's id and what was answered here go, as the iPad's sign-out; the intro comes back.</summary>
    public void Forget()
    {
        generation++;
        (coach, thread) = (null, null);
        answered.Clear();
        notes.Clear();
        cards.Clear();
        homeAsked.Clear();
        grants = null;
        drafts.Clear();
        ThreadItems.Children.Clear();
        IntroError.Visibility = Visibility.Collapsed;
        ThreadView.Visibility = Visibility.Collapsed;
        Intro.Visibility = Visibility.Visible;
        starting = false;
        ShowStart();
    }

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
        AutomationProperties.SetAutomationId(Start, "coach-start");
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
        var gen = generation;
        var (loaded, error, id) = await CoachChat.LoadAsync(App.Words, reader);
        if (gen != generation) return;
        IntroError.Text = error ?? "";
        IntroError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        coach = id;
        if (loaded is null || loaded.Drawn(Kinds).Count == 0) return;
        thread = loaded;
        ShowThread(loaded);
        Intro.Visibility = Visibility.Collapsed;
        ThreadView.Visibility = Visibility.Visible;
        await ReadApprovalsAsync(loaded, gen);
    }

    /// <summary>
    /// What the thread's approvals name, as the iPad's <c>loadSetupThread</c> reads them: planque's grants (each digest
    /// checked), and each KIFF draft not read yet. A read that fails leaves the card saying it's reading the request.
    /// </summary>
    async Task ReadApprovalsAsync(CoachThread loaded, int gen)
    {
        var approvals = loaded.Shown.Where(s => s.Card.Kind == "approval").Select(s => s.Card).ToList();
        if (approvals.Count == 0) return;
        if (Grants is { } readGrants && await readGrants() is { } read && gen == generation) grants = read;
        foreach (var id in approvals.Select(c => c.Draft).OfType<string>().Distinct())
            if (Drafts is { } readDraft && await readDraft(id) is { } draft && gen == generation) drafts[id] = draft;
        if (gen == generation) Redraw();
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
        ShowPlan(thread.Plan);
        // Asked once the thread is drawn: Home's read can answer at once, and it draws the thread again.
        if (Tafs is { } tafs && thread.ReadHomeFor(tafs().Select(t => t.Agent), homeAsked)) DispatcherQueue.TryEnqueue(() => HomeWanted?.Invoke());
        var items = thread.Drawn(Kinds);
        for (var i = 0; i < items.Count; i++)
        {
            if (CoachChat.GapTime(w, items, i, DateTimeOffset.Now, TimeZoneInfo.Local) is { } gap)
                ThreadItems.Children.Add(Words(gap, 12, "TafsDimText", HorizontalAlignment.Center));
            // A card, as the iPad's SetupComponentView: the questions card in this change; the other kinds come in their own.
            if (items[i].Card is { } shown)
            {
                if (shown.Card.Kind == "questions") ThreadItems.Children.Add(QuestionsCard(thread, shown));
                else if (shown.Card.Kind == "notice") ThreadItems.Children.Add(Notice(shown.Card));
                else if (shown.Card.Kind == "result") ThreadItems.Children.Add(Result(thread, shown));
                else if (shown.Card.Kind == "confirm") ThreadItems.Children.Add(Confirm(thread, shown.Card));
                else if (shown.Card.Kind == "approval") ThreadItems.Children.Add(Approval(thread, shown.Card));
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

    /// <summary>
    /// A notice, as the iPad's <c>SetupComponentView.notice</c>: its title at 14 in its tone's colour (the red, the amber, the
    /// green, else soft), its words in the ink, on its tone at 10%.
    /// </summary>
    Border Notice(CoachCard c)
    {
        var tone = c.Tone switch { "error" => "TafsNo", "warning" => "TafsAmber", "success" => "TafsOk", _ => "TafsSoft" };
        var words = new StackPanel { Spacing = 4 };
        if (c.Title is { } title)
        {
            var head = Words(title, 14, "TafsInkText");
            head.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            head.Foreground = Brush(tone);
            words.Children.Add(head);
        }
        words.Children.Add(Words(c.Text ?? "", 14, "TafsInkText"));
        return new Border { Child = words, Padding = new Thickness(12), CornerRadius = new CornerRadius(14), Background = Faint(tone, 0.10) };
    }

    /// <summary>The account's tafs, as Home last listed them: a result card names and opens the taf it handed over.</summary>
    public Func<IReadOnlyList<Tafs.Core.Home.HomeTaf>>? Tafs { get; set; }

    /// <summary>A result card's Open: the taf's page, by its tafs cloud id; Coach closes, as the iPad's beside the sidebar.</summary>
    public event Action<string>? TafChosen;

    /// <summary>A result card's Choose a plan: Plan &amp; usage, where a plan is chosen, as the iPad's <c>usageRequested</c>.</summary>
    public event Action? PlanChosen;

    /// <summary>A hand-over set up whose taf isn't on Home's list yet: the window reads Home again, once for each card.</summary>
    public event Action? HomeWanted;

    /// <summary>The result cards Home was read again for.</summary>
    readonly HashSet<string> homeAsked = [];

    /// <summary>
    /// A result, as the iPad's <c>SetupComponentView.result</c>: a new taf's hand-over, or an improvement's when it names the
    /// taf it improves. Both on the panel with a green edge, their eyebrow in the green, Coach's words at 15.
    /// </summary>
    Border Result(CoachThread thread, ShownCard shown)
    {
        var c = shown.Card;
        var w = App.Words;
        var tafs = Tafs?.Invoke() ?? [];
        var panel = new StackPanel { Spacing = 12 };
        TextBlock Line(string text, double size, string style)
        {
            var line = Words(text, size, style);
            panel.Children.Add(line);
            return line;
        }
        Button Wide(string text, string style)
        {
            var button = new Button { Content = text, Style = (Style)Application.Current.Resources[style], HorizontalAlignment = HorizontalAlignment.Stretch };
            panel.Children.Add(button);
            return button;
        }
        void Eyebrow(string text) => panel.Children.Add(new TextBlock { Text = text.ToUpper(new System.Globalization.CultureInfo(w.Language)),
            Style = (Style)Application.Current.Resources["TafsEyebrow"], Foreground = Brush("TafsOk"), TextWrapping = TextWrapping.Wrap });
        if (c.Taf?.Improves is { } improves)
        {
            // The taf it improves, as the iPad's improvedTaf: by its tafs cloud id, else as an agent, else the card's agent.
            var improved = tafs.FirstOrDefault(t => t.Id == improves) ?? tafs.FirstOrDefault(t => t.Agent == improves) ?? tafs.FirstOrDefault(t => t.Agent == c.Taf.Agent);
            var name = improved?.Name ?? c.Taf.Name;
            Eyebrow(w.Get("coach.result.new_version", ("taf", c.Taf.Name)));
            Line(c.Text ?? "", 15, "TafsInkText");
            if (c.Taf.Note is { Length: > 0 } note) Line(note, 13, "TafsSoftText");
            var (state, version, why) = thread.Improvement(shown.Ref);
            switch (state)
            {
                // Its Versions aren't ported yet: the line the iPad shows when it can't open them.
                case ImprovementState.Made:
                    Line(w.Get("coach.result.version_made", ("version", version.ToString())), 13, "TafsDimText");
                    break;
                case ImprovementState.Failed:
                    Line(CoachChat.NotImproved(w, name, why!), 13, "TafsNoText");
                    Line(w.Get("coach.coach_knows"), 13, "TafsDimText");
                    break;
                default:
                    var preparing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    preparing.Children.Add(new ProgressRing { IsActive = true, Width = 16, Height = 16, Foreground = Brush("TafsSoft") });
                    preparing.Children.Add(new TextBlock { Text = w.Get("coach.result.preparing") });
                    var off = Wide("", "TafsPrimaryOff");
                    off.Content = preparing;
                    off.IsEnabled = false;
                    break;
            }
        }
        else
        {
            // The name the taf got once started: tafs cloud gives a taken name the first free "Rae 2", so Coach's may be another taf's.
            var listed = c.Taf is { } t ? tafs.FirstOrDefault(x => x.Agent == t.Agent) : null;
            var name = listed?.Name ?? c.Taf?.Name ?? "";
            var open = listed is not null && listed.Name != c.Taf?.Name ? w.Get("coach.result.open", ("taf", name)) : c.Yes ?? w.Get("coach.result.see_it", ("taf", name));
            Eyebrow(w.Get("coach.result.set_up", ("taf", name)));
            Line(c.Text ?? "", 15, "TafsInkText");
            if (c.Tools is { Count: > 0 } tools) Line(string.Join(" · ", tools.Select(CoachChat.ToolWords)), 13, "TafsSoftText");
            var (state, why) = thread.HandOver(shown.Ref, listed is not null);
            if (state == HandOverState.SetUp)
            {
                if (thread.PlanNeeded(shown.Ref) is { } words)
                {
                    // Made with no plan, so nothing runs yet: tafs cloud's words, then where a plan is chosen.
                    Line(words, 14, "TafsAmberWords").FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                    Wide(w.Get("usage.choose_plan"), "TafsOutlineButton").Click += (_, _) => PlanChosen?.Invoke();
                }
                if (listed is not null) Wide(open, "TafsPrimary").Click += (_, _) => TafChosen?.Invoke(listed.Id);
                else Line(w.Get("coach.setting_up", ("name", name)), 13, "TafsDimText");
            }
            else if (state == HandOverState.Failed)
            {
                Line(CoachChat.NotSetUp(w, name, why!), 13, "TafsNoText");
                Line(w.Get("coach.coach_knows"), 13, "TafsDimText");
            }
            else Line(w.Get("coach.setting_up", ("name", name)), 13, "TafsDimText");
        }
        return new Border { Style = (Style)Application.Current.Resources["TafsCard"], BorderBrush = Brush("TafsOk"), Child = panel };
    }

    /// <summary>
    /// A confirm, as the iPad's <c>SetupComponentView.confirm</c>: Coach's question at 15 and its options as capsules at 14,
    /// each answering the card with its value; while it's sent, faded and off. Answered, one line: "Live or test? · Test
    /// mode". Off for someone who may not answer Coach, as Send answers is.
    /// </summary>
    StackPanel Confirm(CoachThread thread, CoachCard c)
    {
        var w = App.Words;
        var box = new StackPanel { Spacing = 8 };
        if (thread.AnswerTo(c.Id) is { } reply)
        {
            var chosen = c.Options?.FirstOrDefault(o => o.Value == reply.Text)?.Label ?? reply.Text;
            box.Children.Add(QuestionsCardView.Outcome((c.Text ?? w.Get("coach.questions.the_question")) + " · " + chosen, Brush));
            return box;
        }
        box.Children.Add(Words(c.Text ?? "", 15, "TafsInkText"));
        var busy = answered.Contains(thread.CardRef(c.Id));
        var row = new WrapRow { Spacing = 8 };
        foreach (var option in c.Options ?? [])
        {
            var choice = new Button { Content = option.Label, Style = (Style)Application.Current.Resources["TafsBox"], FontSize = 14, Foreground = Brush("TafsInk"),
                Background = Brush("TafsPanel"), BorderBrush = Faint("TafsInk", 0.18), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), MinHeight = 36,
                Padding = new Thickness(14, 9, 14, 9), IsEnabled = !busy && answer is not null, Opacity = busy ? 0.45 : 1 };
            choice.Click += (_, _) => _ = AnswerAsync(thread, c.Id, option.Value);
            row.Children.Add(choice);
        }
        box.Children.Add(row);
        if (notes.GetValueOrDefault(c.Id) is { } note) box.Children.Add(QuestionsCardView.Note(note, Brush));
        return box;
    }

    /// <summary>The plan pinned above the thread is folded to its step in progress until opened, so the chat comes first.</summary>
    bool planOpen;

    /// <summary>
    /// The plan, as the iPad's <c>SetupPlan</c>: "PLAN · 2 OF 5" and the step it's at, with its mark; a tap opens every step
    /// (the active one in bold, a skipped one struck through, those to come dimmed), and folds it again.
    /// </summary>
    void ShowPlan(ShownCard? plan)
    {
        PlanHost.Visibility = plan is null ? Visibility.Collapsed : Visibility.Visible;
        if (plan is null) return;
        var w = App.Words;
        var steps = plan.Card.Steps ?? [];
        var now = PlanStep.Current(steps);
        var head = new Grid { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = now is null ? null : StepMark(now.Status);
        if (mark is not null) head.Children.Add(mark);
        var words = new StackPanel { Spacing = 2 };
        words.Children.Add(new TextBlock { Style = (Style)Application.Current.Resources["TafsEyebrow"], Text = w.Get("coach.plan.progress",
            ("done", steps.Count(s => s.Status == "done").ToString()), ("total", steps.Count.ToString())).ToUpper(new System.Globalization.CultureInfo(w.Language)) });
        var at = Words(now?.Label ?? plan.Card.Title ?? w.Get("coach.plan_title"), 14, "TafsInkText");
        at.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        at.TextWrapping = TextWrapping.NoWrap;
        at.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Children.Add(at);
        Grid.SetColumn(words, 1);
        head.Children.Add(words);
        var chevron = new FontIcon { FontSize = 12, Foreground = Brush("TafsDim") };
        Grid.SetColumn(chevron, 2);
        head.Children.Add(chevron);
        var toggle = new Button { Content = head, Style = (Style)Application.Current.Resources["TafsQuietButton"], Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var all = new StackPanel { Spacing = 8 };
        foreach (var step in steps)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(StepMark(step.Status));
            var label = Words(step.Label, 14, step.Status is "pending" or "skipped" ? "TafsDimText" : "TafsInkText");
            if (step.Status == "skipped") label.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            if (step.Status == "active") label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            row.Children.Add(label);
            all.Children.Add(row);
        }
        // Folding changes what shows in place, so the button keeps its focus.
        void Fold()
        {
            if (mark is not null) mark.Visibility = planOpen ? Visibility.Collapsed : Visibility.Visible;
            at.Visibility = planOpen ? Visibility.Collapsed : Visibility.Visible;
            all.Visibility = planOpen ? Visibility.Visible : Visibility.Collapsed;
            chevron.Glyph = planOpen ? "\uE70E" : "\uE70D";
            AutomationProperties.SetHelpText(toggle, w.Get(planOpen ? "coach.folds_plan" : "coach.shows_whole_plan"));
        }
        toggle.Click += (_, _) => { planOpen = !planOpen; Fold(); };
        Fold();
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(toggle);
        panel.Children.Add(all);
        PlanHost.Child = new Border { Style = (Style)Application.Current.Resources["TafsCard"], Child = panel };
    }

    /// <summary>
    /// A plan step's mark, as the iPad's <c>StepMark</c>, 18 across: done, a tick on the green; failed, a cross on the red; at
    /// work, a ring in the amber; to do, a faint ring. Narrator reads its status, as the iPad's.
    /// </summary>
    Grid StepMark(string status)
    {
        var mark = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        var circle = new Microsoft.UI.Xaml.Shapes.Ellipse();
        switch (status)
        {
            case "done" or "failed":
                circle.Fill = Brush(status == "done" ? "TafsOk" : "TafsNo");
                mark.Children.Add(circle);
                mark.Children.Add(new FontIcon { Glyph = status == "done" ? "\uE73E" : "\uE711", FontSize = 9, FontWeight = Microsoft.UI.Text.FontWeights.Black,
                    Foreground = Brush("TafsOnFill") });
                break;
            case "active":
                circle.Stroke = Brush("TafsAmber");
                circle.StrokeThickness = 2.5;
                mark.Children.Add(circle);
                break;
            default:
                circle.Stroke = Faint("TafsInk", 0.25);
                circle.StrokeThickness = 1.5;
                mark.Children.Add(circle);
                break;
        }
        AutomationProperties.SetName(mark, status);
        return mark;
    }

    /// <summary>A theme colour at a fraction of its opacity, as the iPad's <c>.opacity</c>.</summary>
    Microsoft.UI.Xaml.Media.SolidColorBrush Faint(string key, double alpha)
    {
        var c = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Brush(key)).Color;
        c.A = (byte)Math.Round(c.A * alpha);
        return new(c);
    }

    /// <summary>The kinds of card drawn so far: the others come in their own changes, their answers in bubbles until then.</summary>
    static readonly string[] Kinds = ["questions", "notice", "result", "confirm", "approval"];

    /// <summary>
    /// An approval, as the iPad's <c>SetupApproval</c>: planque's own words for the connection (its exact terms under
    /// Details), KIFF's draft of the taf's Card as its lines, and where it stands. Answered, one line; a newer request for
    /// the same connection makes it history. Approving comes in its own change: until then Decline and Approve are off.
    /// </summary>
    UIElement Approval(CoachThread thread, CoachCard c)
    {
        var w = App.Words;
        var grant = grants?.FirstOrDefault(g => g.Id == c.Grant);
        var draft = c.Draft is { } id ? drafts.GetValueOrDefault(id) : null;
        var (state, why) = ApprovalCard.State(thread, c, grant);
        if (ApprovalCard.Replaced(thread, c.Id))
            return QuestionsCardView.Outcome(ApprovalCard.ReplacedLine(w, thread, c.Id), Brush, "\uE74B", "TafsDim");
        if (state is ApprovalState.Done or ApprovalState.Declined)
        {
            // What the person approved, under the line: the Card KIFF says it signed.
            var signed = state == ApprovalState.Done && draft is { Signed: true, NoTools: not true } ? draft.CardLines(w).SelectMany(l => l).ToList() : [];
            return QuestionsCardView.Outcome(ApprovalCard.Outcome(w, thread, c, state, grant, draft), Brush,
                state == ApprovalState.Done ? "\uEC61" : "\uECC9", state == ApprovalState.Done ? "TafsOk" : "TafsDim", signed);
        }
        var failed = state == ApprovalState.Failed;
        var box = new StackPanel { Spacing = 10 };
        var upper = new System.Globalization.CultureInfo(w.Language);
        var eyebrow = Words(w.Get(failed ? "coach.answer.failed_short" : "coach.needs_you_approve").ToUpper(upper), 11, "TafsEyebrow");
        eyebrow.Foreground = Brush(failed ? "TafsNo" : "TafsAmber");
        box.Children.Add(eyebrow);
        if (grant is null) box.Children.Add(Words(w.Get("connect.reading_request"), 14, "TafsDimText"));
        else if ((c.ConnectionDone ?? (grant.Connected ? w.Get("coach.connected_keys_kept_sealed", ("vendor", grant.Vendor))
                     : grant.Listed ? w.Get("coach.signed_reading_what_offers", ("vendor", grant.Vendor)) : null)) is { } done)
        {
            var line = Words(done, 14, "TafsInkText");
            (line.Foreground, line.FontWeight) = (Brush("TafsOk"), Microsoft.UI.Text.FontWeights.SemiBold);
            box.Children.Add(line);
        }
        else
        {
            box.Children.Add(Words(grant.Text, 15, "TafsInkText"));
            // The exact terms, for whoever wants them.
            if (grant.Terms is { Count: > 0 } terms)
            {
                var lines = new StackPanel { Spacing = 6 };
                foreach (var term in terms) lines.Children.Add(Mono(term, 11.5, "TafsDim"));
                box.Children.Add(new Expander { Header = w.Get("common.details"), Content = lines, HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left });
            }
            if (grant.Profile is not null) box.Children.Add(Words(w.Get("connect.profile_words_unread"), 13, "TafsSoftText"));
            if (!grant.DigestMatches) box.Children.Add(Words(w.Get("connect.request_does_not_match_what"), 13, "TafsNoText"));
        }
        // The taf's Card as this approval signs it, or, by tafs cloud's mark, as the one approved next: under its words and
        // dimmed, never as if signed now. Marked next with no words, it isn't drawn.
        var next = c.Signs?.CardNext == true;
        if (draft?.Cards is not null && !(next && c.Signs?.Words is null))
        {
            var card = new StackPanel { Spacing = 6 };
            if (next) card.Children.Add(Words(c.Signs!.Words!, 13, "TafsSoftText"));
            else card.Children.Add(Words(draft.CardTitle(w).ToUpper(upper), 11, "TafsEyebrow"));
            foreach (var line in draft.CardLines(w).SelectMany(l => l)) card.Children.Add(Mono(line, 12, next ? "TafsDim" : "TafsInk"));
            box.Children.Add(new Border { Child = card, Padding = new Thickness(12), CornerRadius = new CornerRadius(12), Background = Brush("TafsRaised") });
        }
        else if (grant is not null && ApprovalCard.CardNextWords(c) is { } nextWords) box.Children.Add(Words(nextWords, 13, "TafsSoftText"));
        if (failed) box.Children.Add(Words(why is null ? w.Get("coach.did_not_go_through_nothing") : w.Get("coach.answer.failed_why", ("value", why)), 14, "TafsNoText"));
        // Off until approving is ported: what would be signed is shown, nothing is.
        var buttons = new Grid { ColumnSpacing = 10 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.Children.Add(new Button { Content = w.Get("coach.approval.decline"), Style = (Style)Application.Current.Resources["TafsOutlineButton"], IsEnabled = false });
        var approve = new Button { Content = w.Get("coach.answer.approve"), Style = (Style)Application.Current.Resources["TafsPrimaryOff"], IsEnabled = false };
        Grid.SetColumn(approve, 1);
        buttons.Children.Add(approve);
        box.Children.Add(buttons);
        return new Border
        {
            Child = box, Padding = new Thickness(14), CornerRadius = new CornerRadius(14), Background = Brush("TafsPanel"),
            BorderBrush = Brush(failed ? "TafsNo" : "TafsAmber"), BorderThickness = new Thickness(1),
        };
    }

    TextBlock Mono(string text, double size, string colour) => new()
    {
        Text = text, FontSize = size, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"), Foreground = Brush(colour),
        TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
    };

    QuestionsCardView QuestionsCard(CoachThread thread, ShownCard shown)
    {
        if (!cards.TryGetValue(shown.Ref, out var state)) cards[shown.Ref] = state = new QuestionsCardState();
        // Busy as the iPad's isAnswered: by cardRef, so every card under one id goes quiet together.
        return new QuestionsCardView(shown, thread.AnswerTo(shown.Card.Id), state, answered.Contains(thread.CardRef(shown.Card.Id)),
            notes.GetValueOrDefault(shown.Card.Id), answer is null ? null : text => _ = AnswerAsync(thread, shown.Card.Id, text), Brush);
    }

    /// <summary>
    /// A card's answer, as the iPad's <c>answer(_:_:)</c>: sent as data, the card busy meanwhile. Sent, the thread is read
    /// again (the iPad then follows the run's stream, which comes with writing to Coach); a card that doesn't wait any more
    /// (409) is read again; refused for want of the AI-data consent, the card stays as chosen; else it says it didn't reach.
    /// </summary>
    async Task AnswerAsync(CoachThread thread, string id, string text)
    {
        // The card it's for, as the iPad's cardRef: the one waiting under that id, else the latest; its conversation goes with it.
        if (answer is not { } post || coach is null || thread.CardFor(id) is not { } shown
            || CoachChat.AnswerRequest(coach, shown, text) is not { } request) return;
        var gen = generation;
        answered.Add(shown.Ref);
        notes.Remove(id);
        Redraw();
        var sent = CoachChat.Sent(await post(request.Path, request.Body));
        if (gen != generation) return;
        if (sent != CardSent.Sent) answered.Remove(shown.Ref);
        if (sent == CardSent.NotReached) notes[id] = App.Words.Get("coach.answer_did_not_reach_setup");
        if (sent is CardSent.Sent or CardSent.ReadAgain) await LoadThreadAsync();
        else Redraw();
    }

    /// <summary>The thread drawn again: after an answer, and when Home's list changes, so a result card finds its taf.</summary>
    public void Redraw()
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

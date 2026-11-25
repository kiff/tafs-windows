using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Tafs.Core.Coach;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// Coach mode's conversations and width, as the iPad's <c>SetupChatView</c> toolbar and <c>CoachPanel</c> handle: New
/// conversation (✎), Previous conversations under ⋯ with Archived, the box to write to Coach, and the edge dragged to choose
/// the panel's width, kept with full screen for the next time.
/// </summary>
public sealed partial class CoachPanel
{
    /// <summary>The conversation shown: the newest, as at launch, one reopened, or a new one (the iPad's <c>setupChat</c>, <c>setupFresh</c>).</summary>
    CoachConversation conversation = CoachConversation.Newest;

    /// <summary>The session of the conversation shown, as tafs cloud named it: where the next message goes.</summary>
    string? session;

    /// <summary>Coach was started here: its conversation and box show even before anything is in it.</summary>
    bool begun;

    /// <summary>A message to Coach on its way, one at a time, as the iPad's <c>setupSending</c>.</summary>
    bool coachSending;

    /// <summary>Messages sent here that the thread doesn't have yet, shown after it.</summary>
    List<ChatMessage> coachSent = [];

    /// <summary>Why the last message didn't go, or the conversation couldn't be read, over the box.</summary>
    string? chatError;

    /// <summary>Each New conversation or reopening moves it on, so a read or send for the one before is dropped.</summary>
    int conversationGen;

    /// <summary>The thread read again every 2 s while Coach works or a sent message waits, only while the panel is open.</summary>
    DispatcherTimer coachClock = null!;
    readonly ChatFollow coachFollow = new();

    /// <summary>The window came back: Coach's thread, open, whose following stopped while Coach still works, is read once.</summary>
    internal void CameBack()
    {
        if (IsOpen && !coachSending && !coachClock.IsEnabled && (thread?.Working is not null || coachSent.Count > 0)) _ = LoadThreadAsync();
    }

    /// <summary>The thread's items as drawn, by id, so a read again keeps their order; none for a conversation not drawn yet.</summary>
    List<string>? drawnOrder;

    /// <summary>The width the person chose, kept for the next time.</summary>
    CoachWidth chosen = CoachWidth.Opening;

    static readonly string WidthFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tafs", "coach-width");

    void StartChats()
    {
        var w = App.Words;
        AutomationProperties.SetName(NewChat, w.Get("coach.new_conversation"));
        ToolTipService.SetToolTip(NewChat, w.Get("coach.new_conversation"));
        AutomationProperties.SetName(MoreChats, w.Get("coach.conversations"));
        ToolTipService.SetToolTip(MoreChats, w.Get("coach.conversations"));
        PreviousChats.Text = w.Get("coach.chats.previous");
        ToolTipService.SetToolTip(Back, w.Get("coach.close_coach_mode"));
        AutomationProperties.SetName(Handle, w.Get("coach.coach_mode_width"));
        AutomationProperties.SetHelpText(Handle, w.Get("coach.drag_make_wider_narrower"));
        ToolTipService.SetToolTip(Handle, w.Get("coach.drag_make_wider_narrower"));
        ChatsBack.Content = w.Get("coach.chats.back");
        CoachSendButton.Content = w.Get("common.send");
        // Read again while Coach's thread moves: fast, then slower, then not at all (ChatFollow). Minimised, it waits.
        coachClock = new DispatcherTimer { Interval = ChatFollow.Fast };
        coachClock.Tick += (_, _) =>
        {
            if (!IsOpen || coachSending) { coachClock.Stop(); return; }
            if (MainWindow.Hidden) return;
            _ = LoadThreadAsync();
        };
        try { chosen = CoachWidth.Read(File.Exists(WidthFile) ? File.ReadAllText(WidthFile) : null); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        ShowFull();
    }

    /// <summary>Signed out: back to the newest conversation, with nothing sent or chosen here.</summary>
    void ForgetChats()
    {
        conversationGen++;
        (conversation, session, begun, coachSending, chatError, previousAsked, drawnOrder) = (CoachConversation.Newest, null, false, false, null, false, null);
        coachSent.Clear();
        coachUnsent.Clear();
        coachQueued = null;
        coachClock.Stop();
        coachFollow.Reset();
        CoachDraft.Text = "";
        ChatsView.Visibility = Visibility.Collapsed;
        ChatTools.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// What a read of the conversation shows: with messages, or once Coach was started here or a conversation chosen, the
    /// thread (or what Coach is for, when empty) and the box; else the intro stays, as the iPad's before Setup starts.
    /// </summary>
    bool ShowsConversation(CoachThread? loaded) =>
        coach is not null && (loaded is { } t && t.Drawn(Kinds).Count > 0 || begun || conversation != CoachConversation.Newest);

    /// <summary>✎ and ⋯ once Coach is there; the box for someone who may use Coach.</summary>
    void ShowChatTools()
    {
        ChatTools.Visibility = coach is not null && answer is not null ? Visibility.Visible : Visibility.Collapsed;
        var w = App.Words;
        // Coach takes no typing until its tools are approved: while the tools themselves are still to give, the box is one
        // locked line in tafs cloud's words that opens the step, and the row over it isn't drawn twice.
        var locked = coach is not null && answer is not null && tools is { LocksBox: true };
        CoachComposer.Visibility = coach is not null && answer is not null && !locked ? Visibility.Visible : Visibility.Collapsed;
        ToolsLocked.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        ToolsLockedText.Text = tools is { LocksBox: true } t ? t.LockedLine(w) : "";
        AutomationProperties.SetName(ToolsLocked, ToolsLockedText.Text);
        if (locked) ToolsRow.Visibility = Visibility.Collapsed;
        CoachDraft.PlaceholderText = thread?.Working is not null ? w.Get("chat.write_next") : w.Get("coach.ask_anything");
        AutomationProperties.SetName(CoachDraft, CoachDraft.PlaceholderText);
        // While Coach sends or works, Send queues one message (the iPad's setupQueued); a second waits for that one to go.
        var full = coachQueued is not null && (coachSending || CoachBusy);
        CoachSendButton.Content = w.Get(full && coachSending ? "common.sending" : "common.send");
        CoachSendButton.IsEnabled = !full;
        // Stop while Coach works, as the iPad's: its current run is cancelled.
        CoachStopButton.Content = w.Get("chat.stop");
        CoachStopButton.Visibility = thread?.Working is not null && answer is not null ? Visibility.Visible : Visibility.Collapsed;
        CoachStopButton.IsEnabled = !coachStopping;
        ChatError.Text = chatError ?? "";
        ChatError.Visibility = chatError is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// The conversation shown: its thread with what was sent here and isn't in it yet; with nothing in it, what Coach is
    /// for, as the iPad's empty chat ("Ask anything about your tafs").
    /// </summary>
    void ShowConversation(bool toEnd = true)
    {
        var now = DateTimeOffset.UtcNow;
        coachSent = TafChatSend.Merge(thread?.Messages ?? [], coachSent, now).Waiting.ToList();
        if (thread is { } t && t.Drawn(Kinds).Count > 0) ShowThread(t);
        else
        {
            ThreadItems.Children.Clear();
            PlanHost.Visibility = Visibility.Collapsed;
            if (coachSent.Count == 0 && coachUnsent.Count == 0)
            {
                var w = App.Words;
                var empty = new StackPanel { Spacing = 8, Margin = new Thickness(4, 40, 4, 0) };
                // What this conversation is about, as the iPad's startSwitch: a new taf, or the taf on screen (or being improved).
                if (StartSwitch() is { } choice) empty.Children.Add(choice);
                // Board 7, Coach with no taf or one, as the iPad's SetupChatView.starts: its spark beside one card.
                if (TafCount <= 1)
                {
                    var art = Illustrations.CoachFirst();
                    art.Margin = new Thickness(0, 0, 0, 12);
                    empty.Children.Add(art);
                }
                var heading = Words(w.Get("coach.ask_anything"), 22, "TafsInkText");
                heading.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                AutomationProperties.SetHeadingLevel(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
                empty.Children.Add(heading);
                empty.Children.Add(Words(w.Get("coach.ask_anything_line"), 15, "TafsSoftText"));
                // tafs cloud's recipes by category, for a new taf (not while improving one), as the iPad's starts: a click puts
                // the job's words in the box, to edit and send; it never sends.
                if (improving is null && Recipes is { } jobs) AddRecipes(empty, jobs);
                // Improving a taf, as the iPad's improveExamples: three questions that go at a click.
                if (improving is not null)
                    foreach (var key in new[] { "coach.improve.how_is_it_going", "coach.improve.why_refused", "coach.improve.cant_find_work" })
                        empty.Children.Add(ImproveExample(w.Get(key)));
                ThreadItems.Children.Add(empty);
            }
        }
        foreach (var m in coachSent)
            ThreadItems.Children.Add(ChatPinner.Own(new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(48, 0, 0, 0),
                Children = { ChatViews.OwnerBubble(m.Text, Brush) },
            }, m.Text));
        // A run that ended without an answer: tafs cloud's words, last; Try again only when tafs cloud says it's safe, once.
        if (thread?.Failed is { } failed && coachSent.Count == 0)
            ThreadItems.Children.Add(ChatViews.FailedLine(App.Words, failed,
                FailedRetry.Of(failed, thread.Messages, coachFailedRetried, coachSending || CoachBusy), Brush, RetryCoachFailed));
        // How the last run ended, when the person stopped it, as the iPad's ended: "Stopped", until the next message.
        if (coachStopped && thread?.Working is null)
            ThreadItems.Children.Add(Words(App.Words.Get("taf.state.stopped"), 13, "TafsDimText"));
        // What was written while Coach works, as the iPad's QueuedBubble: faded, "Sends when this answer is done", Don't send.
        if (coachQueued is { } queued) ThreadItems.Children.Add(ChatPinner.Own(QueuedBubble(queued), queued));
        // What tafs cloud didn't take, after it, where it was written.
        foreach (var m in coachUnsent)
            ThreadItems.Children.Add(ChatPinner.Own(ChatViews.UnsentBubble(App.Words, m, Brush, coachSending,
                () => RetryCoachUnsent(m), () => EditCoachUnsent(m), () => DeleteCoachUnsent(m),
                // Refused because the account is halted: Resume opens Halt all, Coach mode closing first.
                Unsent.ResumesWith(m, inTafChat: false) is HaltScreen.Account ? () => { Close(); ChatViews.ChooseResume(null, ""); } : null), m.Text));
        Intro.Visibility = Visibility.Collapsed;
        ChatsView.Visibility = Visibility.Collapsed;
        ThreadView.Visibility = Visibility.Visible;
        ShowChatTools();
        // Coach done: what was queued goes now, as its next message.
        if (coachQueued is { } next && !CoachBusy && !coachSending)
        {
            coachQueued = null;
            DispatcherQueue.TryEnqueue(async () =>
            {
                Pinner.Next(next);
                if (await SendCoachAsync(next) is null) { Pinner.Clear(); if (CoachDraft.Text.Length == 0) CoachDraft.Text = next; }
            });
        }
        // Read again while Coach works, a sent message waits, or a new version is still being prepared.
        // Each read says how long until the next (ChatFollow): none once it has been quiet half an hour.
        if ((thread?.Working is not null || coachSent.Count > 0 || thread?.AwaitsImprovement(DateTimeOffset.UtcNow) == true)
            && coachFollow.Next(ChatFollow.Signature(thread, coachSent.Count), DateTimeOffset.UtcNow, Random.Shared.NextDouble()) is { } wait)
        {
            coachClock.Interval = wait;
            coachClock.Start();
        }
        else
        {
            coachClock.Stop();
            coachFollow.Reset();
        }
        // What was just sent at the top, Coach's answer growing under it, as the iPad's SetupChatView; else the thread's end.
        if (!Pinner.Pin() && toEnd) DispatcherQueue.TryEnqueue(() =>
        {
            ThreadScroll.UpdateLayout();
            ThreadScroll.ChangeView(null, ThreadScroll.ScrollableHeight, null, disableAnimation: true);
        });
    }

    ChatPinner? pinner;

    /// <summary>Coach's pin (<see cref="ChatPinner"/>): its thread's scroll and panel, the scroll's 16 under it, only while it shows.</summary>
    ChatPinner Pinner => pinner ??= new ChatPinner(ThreadScroll, ThreadItems, 16, () => IsOpen && ThreadView.Visibility == Visibility.Visible);

    /// <summary>
    /// New conversation, as the iPad's ✎: an empty one, and the next message starts it. One that's already empty stays,
    /// with what's typed.
    /// </summary>
    void OnNewChat(object sender, RoutedEventArgs e)
    {
        StartNewConversation();
        CoachDraft.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Coach with words in its box for the person to edit and send, nothing sent: in a new conversation, as the iPad's
    /// <c>handRecipeToCoach</c> (a job's Try it with Coach), or in the one shown, as its <c>startNextMove</c> (a brief's Start).
    /// </summary>
    public void Write(string words, bool newConversation)
    {
        Open();
        if (newConversation)
        {
            StartNewConversation();
            // Coach's id, so the new conversation and its box show, even when the panel hadn't read it yet.
            _ = LoadThreadAsync();
        }
        CoachDraft.Text = words;
        DispatcherQueue.TryEnqueue(() =>
        {
            CoachDraft.Focus(FocusState.Programmatic);
            CoachDraft.SelectionStart = CoachDraft.Text.Length;
        });
    }

    /// <summary>A new, empty conversation; one that's already empty stays, with what's typed.</summary>
    void StartNewConversation()
    {
        // Another conversation: nothing of the last one's is pinned.
        pinner?.Clear();
        // A new conversation starts with an empty tray.
        coachTray?.Clear();
        // A new conversation improves no taf, as the iPad's newSetupChat.
        improving = null;
        ShowImproving();
        var empty = conversation.Fresh && coachSent.Count == 0 && !coachSending;
        if (!empty)
        {
            conversationGen++;
            (conversation, session, thread, chatError, coachSending, drawnOrder) = (CoachConversation.New, null, null, null, false, null);
            coachSent.Clear();
            coachUnsent.Clear();
            coachQueued = null;
            answered.Clear();
            notes.Clear();
            cards.Clear();
            grants = null;
            drafts.Clear();
            draftErrors.Clear();
            ShowConversation();
        }
    }

    /// <summary>
    /// Coach on one of its own chats, as the iPad's <c>finishSetup</c>: Finish setting up opens it where the setup stopped.
    /// Reading that chat is what makes the taf at tafs cloud; the next Home read lists it with the tafs.
    /// </summary>
    public void OpenOn(string chosenSession)
    {
        OpenChat(chosenSession);
        Open();
    }

    /// <summary>The chat open on screen: Coach open on a chat that has one; none otherwise (the iPad's coachChatOnScreen).</summary>
    public string? ChatOnScreen => IsOpen ? session : null;

    /// <summary>A chat that was set aside isn't shown any more: its next message would go on in an ended chat. A new one instead.</summary>
    public void Leave(IReadOnlySet<string> ended)
    {
        if (session is { } shown && ended.Contains(shown)) StartNewConversation();
    }

    /// <summary>How many tafs Home lists, as the window last read it: Coach's empty chat draws board 7 with none or one.</summary>
    public int TafCount { get; set; }

    /// <summary>A conversation from the list, reopened: shown alone, and messages go on in it, as the iPad's <c>openSetupChat</c>.</summary>
    void OpenChat(string chosenSession)
    {
        pinner?.Clear();
        conversationGen++;
        (conversation, session, thread, chatError, coachSending, drawnOrder) = (CoachConversation.Of(chosenSession), chosenSession, null, null, false, null);
        coachSent.Clear();
        coachUnsent.Clear();
        coachQueued = null;
        ChatsView.Visibility = Visibility.Collapsed;
        ThreadItems.Children.Clear();
        ThreadView.Visibility = Visibility.Visible;
        _ = LoadThreadAsync();
    }

    /// <summary>Enter sends; Shift+Enter is a new line, as a taf's chat.</summary>
    void OnCoachDraftKey(object sender, KeyRoutedEventArgs e)
    {
        // Enter sends; Shift+Enter is a new line (seen before the box takes the Enter).
        if (ChatViews.SendsOnEnter(e)) { e.Handled = true; OnCoachSend(sender, e); }
    }

    /// <summary>
    /// Send, as the iPad's <c>deliverSetupOnce</c> without a stream: checked first (nothing to send says nothing; too long
    /// says so), shown at once, then sent into the conversation shown, or a new one. The conversation it went into is the
    /// one shown from then on. Not taken, it stays in the thread as not sent, with why, Edit and Retry (<see cref="Unsent"/>).
    /// </summary>
    async void OnCoachSend(object sender, RoutedEventArgs e)
    {
        // Send waits while a file is going up; a message may be words, files, or both.
        if (CoachTray.Uploading) return;
        var (text, why) = TafChatSend.Check(App.Words, CoachDraft.Text);
        if (text is null && CoachDraft.Text.Trim().Length == 0 && CoachTray.Ready.Count > 0) (text, why) = ("", null);
        if (text is null)
        {
            chatError = why;
            ShowChatTools();
            return;
        }
        // Coach still sending or working: held as its next message, shown faded, sent once it's done (the iPad's setupQueued).
        if ((coachSending || CoachBusy) && coachQueued is null && CoachTray.Ready.Count == 0)
        {
            coachQueued = text;
            CoachDraft.Text = "";
            ShowConversation();
            return;
        }
        // Out of the box at once, as it shows in the thread, and to the top; not tried at all, back in the box unless something
        // new is there, and nothing pinned.
        CoachDraft.Text = "";
        Pinner.Next(text);
        if (await SendCoachAsync(text) is null)
        {
            Pinner.Clear();
            if (CoachDraft.Text.Length == 0) CoachDraft.Text = text;
        }
    }

    /// <summary>
    /// A message to Coach: null when it wasn't tried (already sending, or no Coach to send it to); true once sent; false
    /// when tafs cloud didn't take it, and the thread keeps it as not sent.
    /// </summary>
    /// <param name="messageId">The id it was sent under before (Retry); none for a new message, which gets its own.</param>
    async Task<bool?> SendCoachAsync(string text, string? messageId = null)
    {
        if (coachSending || coach is not { } id || answer is not { } send) return null;
        var w = App.Words;
        var (gen, mine) = (generation, conversationGen);
        var pending = TafChatSend.Pending(text, thread?.Messages ?? [], DateTimeOffset.UtcNow);
        coachSent.Add(pending);
        // The box is the Send button's to clear: Retry leaves what's being typed there alone, as the iPad's submitSetup.
        (coachSending, chatError, coachStopped) = (true, null, false);
        ShowConversation();
        var files = CoachTray.Ready;
        // Improving a taf: its first message goes to that taf's improve route, as the iPad's; then Coach's as usual.
        var improves = improving;
        // Coach's messages carry their id; the improve route takes none, so a taf's first message to Coach goes without.
        var mid = improves is null ? messageId ?? TafChatSend.NewMessageId() : null;
        var (into, refused) = improves is { } taf ? await CoachSend.ImproveAsync(w, taf.Id, text, send, files)
            : await CoachSend.SendAsync(w, id, text, conversation, send, files, mid);
        if (refused is null && files.Count > 0) CoachTray.Sent(files);
        if (improves is not null && refused is null)
        {
            improving = null;
            ShowImproving();
        }
        if (gen != generation || mine != conversationGen) return null;
        coachSending = false;
        if (refused is { Consent: true })
        {
            // Refused for want of the AI-data consent: its screen says why, and the words go back in the box, as the iPad's.
            coachSent.Remove(pending);
            ShowConversation();
            return null;
        }
        if (refused is not null)
        {
            coachSent.Remove(pending);
            coachUnsent.Add(UnsentMessage.After(text, refused, DateTimeOffset.UtcNow, mid));
            ShowConversation();
            return false;
        }
        // The conversation it went into, a new one included (new_chat, or after a hand-over), is the one shown from now on.
        if (into != session || conversation.Fresh)
        {
            conversationGen++;
            (conversation, session) = (CoachConversation.Of(into!), into);
            // Another conversation: drawn as read.
            if (thread is not null && into != thread.Working && thread.Messages.All(m => m.Session != into)) drawnOrder = null;
        }
        await LoadThreadAsync();
        return true;
    }

    /// <summary>The failed run whose Try again was pressed: pressed once, it sends once, and the button goes for that run.</summary>
    string? coachFailedRetried;

    /// <summary>
    /// Try again under a failed run's line (tafs cloud's <c>retry</c>), as Android's <c>retryFailed</c>: the person's words for
    /// that run go to Coach again as a new message, once; not tried, the words go back in the box, as any message's do.
    /// </summary>
    async void RetryCoachFailed(FailedRetry again)
    {
        if (coachFailedRetried == again.Run) return;
        coachFailedRetried = again.Run;
        Pinner.Next(again.Text);
        if (await SendCoachAsync(again.Text) is null)
        {
            Pinner.Clear();
            if (CoachDraft.Text.Length == 0) CoachDraft.Text = again.Text;
        }
    }

    bool coachStopping;
    /// <summary>The person stopped Coach's last run: "Stopped" under the thread until the next message goes.</summary>
    bool coachStopped;

    /// <summary>Stop, as the iPad's <c>stopSetup</c>: the run at work in Coach's conversation is cancelled; not taken, why.</summary>
    async void OnCoachStop(object sender, RoutedEventArgs e)
    {
        if (coachStopping || coach is not { } id || answer is not { } send || thread?.Working is not { } working) return;
        coachStopping = true;
        ShowChatTools();
        var gen = generation;
        var why = await TafChatSend.StopAsync(App.Words, id, working, send);
        // Reset first: a chat or account switch meanwhile must never leave Stop off for good.
        coachStopping = false;
        if (gen != generation) return;
        (coachStopped, chatError) = (why is null, why);
        await LoadThreadAsync();
        ShowConversation();
    }

    /// <summary>The one message written while Coach sends or works, sent once it's done; none when nothing waits.</summary>
    string? coachQueued;

    /// <summary>Coach is at work on an answer (tafs cloud's <c>working</c>): a new message waits for it.</summary>
    bool CoachBusy => thread?.Working is not null;

    /// <summary>The queued message, as the iPad's <c>QueuedBubble</c>: the person's bubble faded, its line under it, and Don't send in its menu.</summary>
    StackPanel QueuedBubble(string text)
    {
        var w = App.Words;
        var bubble = ChatViews.OwnerBubble(text, Brush);
        bubble.Opacity = 0.6;
        var box = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(48, 0, 0, 0) };
        box.Children.Add(bubble);
        box.Children.Add(new TextBlock { Text = w.Get("chat.queued.sends_when_done", ("who", w.Get("chat.queued.this_answer"))), FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Right, Foreground = Brush("TafsDim") });
        var dontSend = new MenuFlyoutItem { Text = w.Get("chat.queued.dont_send") };
        dontSend.Click += (_, _) =>
        {
            // Back in the box, unless something new is there: taken back, never lost.
            if (CoachDraft.Text.Length == 0) CoachDraft.Text = text;
            coachQueued = null;
            ShowConversation();
        };
        box.ContextFlyout = new MenuFlyout { Items = { dontSend } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "coach.queued");
        return box;
    }

    AttachTray? coachTray;

    /// <summary>Coach's tray, as a taf's chat: files declared for Coach's own taf.</summary>
    AttachTray CoachTray => coachTray ??= new AttachTray(CoachFileTray, CoachAttach, () => this,
        () => CoachSendButton.IsEnabled = !coachTray!.Uploading && !coachSending);

    async void OnCoachAttach(object sender, RoutedEventArgs e)
    {
        if (coach is { } id && answer is { } send) await CoachTray.PickAsync(id, send);
    }

    /// <summary>tafs cloud's jobs (<c>GET /v1/recipes/gallery</c>), for an empty conversation's list; none before they're read.</summary>
    public Tafs.Core.Home.Gallery? Recipes
    {
        get => recipes;
        set
        {
            recipes = value;
            if (IsOpen) Redraw();
        }
    }

    Tafs.Core.Home.Gallery? recipes;

    /// <summary>
    /// The jobs under "Ask Coach anything", as the iPad's <c>SetupChatView.starts</c>: each category's title in capitals, then
    /// each of its jobs, by what leads it (the problem it ends, else its title) over its line. Only a category with a job shows.
    /// </summary>
    void AddRecipes(StackPanel empty, Tafs.Core.Home.Gallery jobs)
    {
        var language = new System.Globalization.CultureInfo(App.Words.Language);
        foreach (var category in jobs.Categories)
        {
            var listed = jobs.Recipes.Where(r => r.Category == category.Id).ToList();
            if (listed.Count == 0) continue;
            var title = Words(category.Title.ToUpper(language), 12, "TafsDimText");
            title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            title.Margin = new Thickness(0, 10, 0, 0);
            AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level3);
            empty.Children.Add(title);
            foreach (var job in listed)
            {
                var lines = new StackPanel { Spacing = 2 };
                var lead = Words(job.Lead, 15, "TafsInkText");
                lead.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                lines.Children.Add(lead);
                if (job.Line.Trim().Length > 0) lines.Children.Add(Words(job.Line, 13, "TafsSoftText"));
                var row = new Button
                {
                    Content = lines, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(14, 12, 14, 12), Background = Brush("TafsPanel"), BorderBrush = Brush("TafsLine"),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                };
                AutomationProperties.SetName(row, job.Line.Trim().Length > 0 ? $"{job.Lead}. {job.Line}" : job.Lead);
                row.Click += (_, _) =>
                {
                    CoachDraft.Text = job.Say;
                    CoachDraft.Focus(FocusState.Programmatic);
                    CoachDraft.SelectionStart = CoachDraft.Text.Length;
                };
                empty.Children.Add(row);
            }
        }
    }

    /// <summary>The taf on screen beside Coach (its tafs cloud id and name), for the start switch; none when no taf's page shows.</summary>
    public Func<(string Id, string Name)?>? TafOnScreen { get; set; }

    /// <summary>
    /// The start switch, as the iPad's <c>startSwitch</c> (kiff/tafs#138): New taf, or Improve the taf on screen (or the one being
    /// improved), the chosen one filled in Coach's colour. Only when there's a taf to choose.
    /// </summary>
    UIElement? StartSwitch()
    {
        var w = App.Words;
        var onScreen = TafOnScreen?.Invoke();
        if ((improving ?? onScreen) is not { } taf) return null;
        Button Choice(string title, bool chosen, Action act)
        {
            var b = new Button
            {
                Content = new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                Style = (Style)Application.Current.Resources["TafsQuietButton"], CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 8, 14, 8),
                Background = chosen ? Brush("TafsCoach") : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Foreground = chosen ? Brush("TafsOnFill") : Brush("TafsCoach"), IsEnabled = !coachSending,
            };
            AutomationProperties.SetName(b, title);
            if (chosen) AutomationProperties.SetItemStatus(b, "selected");
            b.Click += (_, _) => { if (!chosen) act(); };
            return b;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, HorizontalAlignment = HorizontalAlignment.Left };
        row.Children.Add(Choice(w.Get("home.new_taf"), improving is null, () =>
        {
            improving = null;
            ShowImproving();
            ShowConversation();
        }));
        row.Children.Add(Choice(w.Get("coach.improve_taf", ("taf", taf.Name)), improving is not null, () => Improve(taf.Id, taf.Name)));
        var box = new Border { Child = row, Padding = new Thickness(3), CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(1),
            BorderBrush = Brush("TafsLine"), Background = Brush("TafsPanel"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(box, w.Get("coach.what_conversation_about"));
        return box;
    }

    /// <summary>One of the improve examples: its words go as the first message to the taf's improve route, as a click on the iPad's.</summary>
    Button ImproveExample(string words)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = words, FontSize = 15, TextWrapping = TextWrapping.Wrap },
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 12, 14, 12),
            Style = (Style)Application.Current.Resources["TafsQuietButton"], CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
            BorderBrush = Brush("TafsLine"), Background = Brush("TafsPanel"), IsEnabled = !coachSending,
        };
        AutomationProperties.SetName(b, words);
        b.Click += async (_, _) =>
        {
            Pinner.Next(words);
            if (await SendCoachAsync(words) is null) Pinner.Clear();
        };
        return b;
    }

    /// <summary>The taf a new conversation improves, as the iPad's <c>setupImproving</c>: its id and name; none once its first message went.</summary>
    (string Id, string Name)? improving;

    /// <summary>
    /// Improve a taf with Coach, as the iPad's <c>improveTaf</c>: Coach opens on a new conversation about that taf, "Improve
    /// Refunds" over its box and the iPad's line under it; its first message goes to the taf's own improve route. A new
    /// conversation, or another taf's, ends it.
    /// </summary>
    public void Improve(string taf, string name)
    {
        Open();
        StartNewConversation();
        improving = (taf, name);
        _ = LoadThreadAsync();
        ShowImproving();
        DispatcherQueue.TryEnqueue(() => CoachDraft.Focus(FocusState.Programmatic));
    }

    void ShowImproving()
    {
        var w = App.Words;
        ImprovingNote.Visibility = improving is null ? Visibility.Collapsed : Visibility.Visible;
        if (improving is not { } taf) return;
        ImprovingTitle.Text = w.Get("coach.improve_taf", ("taf", taf.Name));
        ImprovingLine.Text = w.Get("coach.improve_line", ("name", taf.Name));
    }

    /// <summary>Messages tafs cloud didn't take in the conversation shown, kept after it until Retry, Edit or Delete takes them.</summary>
    List<UnsentMessage> coachUnsent = [];

    /// <summary>Retry, as the iPad's <c>retrySetup</c>: sent again once; not tried at all, it goes back in its place.</summary>
    async void RetryCoachUnsent(UnsentMessage m)
    {
        if (coachSending) return;
        (coachUnsent, var at) = Unsent.Take(coachUnsent, m.Id);
        // Under the id it was sent under: if the first send reached tafs cloud after all, it's that message, not a second.
        if (await SendCoachAsync(m.Text, m.MessageId) is null) coachUnsent = Unsent.PutBack(coachUnsent, m, at);
        ShowConversation();
    }

    /// <summary>Edit, as the iPad's <c>editUnsent</c>: out of the thread, its words in the box to change.</summary>
    void EditCoachUnsent(UnsentMessage m)
    {
        coachUnsent = Unsent.Take(coachUnsent, m.Id).Left;
        CoachDraft.Text = m.Text;
        CoachDraft.Focus(FocusState.Programmatic);
        CoachDraft.SelectionStart = CoachDraft.Text.Length;
        ShowConversation(toEnd: false);
    }

    void DeleteCoachUnsent(UnsentMessage m)
    {
        coachUnsent = Unsent.Take(coachUnsent, m.Id).Left;
        ShowConversation(toEnd: false);
    }

    /// <summary>Previous conversations, as the iPad's ⋯ menu: newest first, then Archived.</summary>
    void OnPreviousChats(object sender, RoutedEventArgs e) => ShowPreviousChats();

    /// <summary>Previous conversations, from ⋯, or opened on it for a screenshot (<c>--page coach-chats</c>).</summary>
    internal void ShowPreviousChats()
    {
        // Asked before Coach's id is read: it opens once it is.
        previousAsked = coach is null;
        if (!previousAsked) _ = ShowChatsAsync(archived: false);
    }

    bool previousAsked;

    bool showingArchived;

    void OnChatsBack(object sender, RoutedEventArgs e)
    {
        if (showingArchived || watchDayShown is not null) { watchDayShown = null; _ = ShowChatsAsync(archived: false); return; }
        ChatsView.Visibility = Visibility.Collapsed;
        if (ShowsConversation(thread)) ShowConversation();
        else
        {
            ThreadView.Visibility = Visibility.Collapsed;
            Intro.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// The list, as the iPad's <c>SetupChatsList</c> (or <c>ArchivedSetupChats</c>): each conversation by what was asked first
    /// and when it was last updated, the one shown marked; a tap reopens it. Archive asks first ("Archive this
    /// conversation?"), and Put back returns one; either not taken says why at the top. Archived comes at the end.
    /// </summary>
    async Task ShowChatsAsync(bool archived, string? why = null)
    {
        if (coach is not { } id || read is not { } reader) return;
        var w = App.Words;
        var gen = generation;
        showingArchived = archived;
        ChatsTitle.Text = w.Get(archived ? "coach.chats.archived" : "coach.chats.previous");
        Intro.Visibility = Visibility.Collapsed;
        ThreadView.Visibility = Visibility.Collapsed;
        ChatsView.Visibility = Visibility.Visible;
        // Focus stays in the list as it's drawn again: on its Back.
        ChatsBack.Focus(FocusState.Programmatic);
        ChatsItems.Children.Clear();
        ChatsItems.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20, Margin = new Thickness(0, 20, 0, 0) });
        var (rows, unread, deleteSaid) = await CoachChats.ReadAsync(w, archived ? CoachChats.ArchivedPath(id) : CoachChats.ListPath(id), reader);
        // Delete's words, kept from the last read that sent them, so its failed line is on hand when the network isn't.
        if (deleteSaid is not null) deleteWords = deleteSaid;
        if (gen != generation || showingArchived != archived || ChatsView.Visibility != Visibility.Visible) return;
        ChatsItems.Children.Clear();
        if ((why ?? unread) is { } said) ChatsItems.Children.Add(Words(said, 13, "TafsNoText"));
        foreach (var row in rows ?? [])
            ChatsItems.Children.Add(ChatRow(id, row, archived));
        if (rows is { Count: 0 })
            ChatsItems.Children.Add(Words(w.Get(archived ? "coach.chats.nothing_archived" : "coach.chats.none"), 14, "TafsDimText", HorizontalAlignment.Center));
        // From your watch (W11), between the chats and Archived, only when there are days.
        if (!archived) await AddWatchDaysAsync(gen);
        if (!archived)
        {
            var more = new Button
            {
                Content = w.Get("coach.chats.archived"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14, 12, 14, 12), Style = (Style)Application.Current.Resources["TafsQuietButton"],
            };
            more.Click += (_, _) => _ = ShowChatsAsync(archived: true);
            ChatsItems.Children.Add(new Border { Child = more, CornerRadius = new CornerRadius(12), Background = Brush("TafsPanel") });
        }
    }

    /// <summary>One conversation: its title (two lines at most) and when, a tap to open it, and Archive or Put back beside it.</summary>
    UIElement ChatRow(string id, CoachChatRow row, bool archived)
    {
        var w = App.Words;
        var words = new StackPanel { Spacing = 4 };
        var title = Words(row.Title, 15, "TafsInkText");
        title.MaxLines = 2;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        words.Children.Add(title);
        words.Children.Add(Words(CoachChats.When(w, row.Updated, TimeZoneInfo.Local), 12, "TafsDimText"));
        var open = new Button
        {
            Content = words, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 10, 8, 10), Style = (Style)Application.Current.Resources["TafsQuietButton"],
        };
        AutomationProperties.SetName(open, $"{row.Title}, {CoachChats.When(w, row.Updated, TimeZoneInfo.Local)}");
        open.Click += (_, _) => OpenChat(row.Session);
        var grid = new Grid
        {
            ColumnSpacing = 4, CornerRadius = new CornerRadius(12),
            Background = Brush(!archived && row.Session == session ? "TafsRaised" : "TafsPanel"),
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(open);
        if (answer is { } send)
        {
            var act = new Button
            {
                Content = w.Get(archived ? "coach.chats.put_back" : "coach.chats.archive"), FontSize = 13, Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("TafsSoft"), Style = (Style)Application.Current.Resources["TafsQuietButton"],
            };
            AutomationProperties.SetName(act, $"{act.Content}: {row.Title}");
            Grid.SetColumn(act, 1);
            act.Click += async (_, _) =>
            {
                if (!archived && !await ConfirmArchiveAsync()) return;
                act.IsEnabled = false;
                var gen = generation;
                var why = archived ? await CoachChats.PutBackAsync(w, id, row.Session, send) : await CoachChats.ArchiveAsync(w, id, row.Session, send);
                if (gen != generation) return;
                // The one shown, archived, gives way to a new conversation, as the iPad's.
                if (why is null && !archived && row.Session == session) ForgetShownConversation();
                await ShowChatsAsync(archived, why);
            };
            grid.Children.Add(act);
        }
        // Delete, only in tafs cloud's words: the item shows once a list read has sent them.
        if (Delete is { } delete && deleteWords is { } said)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bin = new Button
            {
                Content = new FontIcon { Glyph = "E74D", FontSize = 14 }, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("TafsSoft"), Style = (Style)Application.Current.Resources["TafsQuietButton"],
            };
            ToolTipService.SetToolTip(bin, said.Item);
            AutomationProperties.SetName(bin, $"{said.Item}: {row.Title}");
            AutomationProperties.SetAutomationId(bin, "chats.delete");
            Grid.SetColumn(bin, 2);
            bin.Click += async (_, _) =>
            {
                if (!await ConfirmDeleteAsync(said, row.Title)) return;
                bin.IsEnabled = false;
                var gen = generation;
                // Gone, the files opened from a chat and the last chat export go with it, as at sign-out.
                var done = await CoachChats.DeleteAsync(w, said, id, row.Session, delete,
                    [AttachTray.Folder, global::Tafs.Core.Account.ChatsExport.Folder]);
                if (gen != generation) return;
                if (done.Gone && row.Session == session) ForgetShownConversation();
                await ShowChatsAsync(archived, done.Why);
            };
            grid.Children.Add(bin);
        }
        return grid;
    }

    /// <summary>tafs cloud's Delete words, from the last list read that sent them; none until one does.</summary>
    ChatDeleteWords? deleteWords;

    /// <summary>
    /// How the panel deletes a conversation for good (<c>DELETE</c>, as the session): only for someone who may use Coach; none
    /// for the sample. Without it, or without tafs cloud's words, there's no Delete.
    /// </summary>
    public Func<string, Task<CloudRead>>? Delete { get; set; }

    /// <summary>The conversation shown, archived or deleted, gives way to a new one, as the iPad's.</summary>
    void ForgetShownConversation()
    {
        conversationGen++;
        (conversation, session, thread, drawnOrder) = (CoachConversation.New, null, null, null);
        coachSent.Clear();
        coachUnsent.Clear();
        coachQueued = null;
    }

    /// <summary>
    /// Delete's question, by name, in tafs cloud's words: its title, the conversation's, what goes and what stays, then its
    /// button or Cancel. Nothing is deleted before this yes.
    /// </summary>
    async Task<bool> ConfirmDeleteAsync(ChatDeleteWords said, string title)
    {
        var body = new StackPanel { Spacing = 8 };
        var name = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        body.Children.Add(name);
        body.Children.Add(new TextBlock { Text = said.ConfirmBody, TextWrapping = TextWrapping.Wrap });
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = said.ConfirmTitle, Content = body,
            PrimaryButtonText = said.ConfirmButton, CloseButtonText = App.Words.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
        };
        return await ask.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>"Archive this conversation?", and what it does, as the iPad's confirmation: Archive or Cancel.</summary>
    async Task<bool> ConfirmArchiveAsync()
    {
        var w = App.Words;
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = w.Get("coach.chats.archive_title"),
            Content = new TextBlock { Text = w.Get("coach.chats.archive_body"), TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = w.Get("coach.chats.archive"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
        };
        return await ask.ShowAsync() == ContentDialogResult.Primary;
    }

    // The edge, dragged: as the iPad's handle, wider to the left, narrower to the right, closed well past a third.

    double? dragFrom;

    void OnHandlePressed(object sender, PointerRoutedEventArgs e)
    {
        if (running is not null) return;
        dragFrom = e.GetCurrentPoint(Over).Position.X;
        Handle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    void OnHandleMoved(object sender, PointerRoutedEventArgs e)
    {
        if (dragFrom is not { } from) return;
        PanelColumn.Width = new GridLength(CoachScreen.Dragging(Over.ActualWidth, chosen, e.GetCurrentPoint(Over).Position.X - from));
        e.Handled = true;
    }

    void OnHandleReleased(object sender, PointerRoutedEventArgs e)
    {
        if (dragFrom is not { } from) return;
        dragFrom = null;
        Handle.ReleasePointerCapture(e.Pointer);
        EndDrag(e.GetCurrentPoint(Over).Position.X - from);
        e.Handled = true;
    }

    void OnHandleLost(object sender, PointerRoutedEventArgs e)
    {
        if (dragFrom is null) return;
        dragFrom = null;
        Fit(Over.ActualWidth);
    }

    void EndDrag(double dragged)
    {
        if (CoachScreen.Dragged(Over.ActualWidth, chosen, dragged) is not { } to)
        {
            // Past its narrowest: it closes, and opens again at the width it had.
            Close();
            return;
        }
        chosen = to;
        KeepWidth();
        Fit(Over.ActualWidth);
        ShowFull();
    }

    /// <summary>From the keyboard: Left widens it and Right narrows it, a tenth of the window at a time, never closing it.</summary>
    void OnHandleKey(object sender, KeyRoutedEventArgs e)
    {
        var step = Over.ActualWidth / 10;
        var dragged = e.Key switch { Windows.System.VirtualKey.Left => -step, Windows.System.VirtualKey.Right => step, _ => 0 };
        if (dragged == 0) return;
        e.Handled = true;
        var to = CoachScreen.Dragged(Over.ActualWidth, chosen, dragged) ?? chosen with { Share = CoachScreen.Narrowest, Full = false };
        chosen = to;
        KeepWidth();
        Fit(Over.ActualWidth);
        ShowFull();
    }

    void OnHandleEntered(object sender, PointerRoutedEventArgs e) =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

    void OnHandleExited(object sender, PointerRoutedEventArgs e)
    {
        if (dragFrom is null) ProtectedCursor = null;
    }

    /// <summary>⤢'s icon and name, for the width shown.</summary>
    void ShowFull()
    {
        FullIcon.Glyph = chosen.Full ? "" : "";
        AutomationProperties.SetName(FullScreen, App.Words.Get(chosen.Full ? "coach.back_width" : "coach.full_screen"));
        ToolTipService.SetToolTip(FullScreen, App.Words.Get(chosen.Full ? "coach.back_width" : "coach.full_screen"));
    }

    /// <summary>The width kept for the next time, as the iPad's <c>@AppStorage</c>; a file that can't be written keeps it for now.</summary>
    void KeepWidth()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WidthFile)!);
            File.WriteAllText(WidthFile, chosen.Kept());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

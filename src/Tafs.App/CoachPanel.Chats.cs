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
        coachClock = new DispatcherTimer { Interval = TafChatSend.WhileWorking };
        coachClock.Tick += (_, _) => { if (IsOpen && !coachSending) _ = LoadThreadAsync(); else coachClock.Stop(); };
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
        coachClock.Stop();
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
        CoachComposer.Visibility = coach is not null && answer is not null ? Visibility.Visible : Visibility.Collapsed;
        CoachDraft.PlaceholderText = thread?.Working is not null ? w.Get("chat.write_next") : w.Get("coach.ask_anything");
        AutomationProperties.SetName(CoachDraft, CoachDraft.PlaceholderText);
        CoachSendButton.Content = w.Get(coachSending ? "common.sending" : "common.send");
        CoachSendButton.IsEnabled = !coachSending;
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
                var heading = Words(w.Get("coach.ask_anything"), 22, "TafsInkText");
                heading.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                AutomationProperties.SetHeadingLevel(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
                empty.Children.Add(heading);
                empty.Children.Add(Words(w.Get("coach.ask_anything_line"), 15, "TafsSoftText"));
                ThreadItems.Children.Add(empty);
            }
        }
        foreach (var m in coachSent)
            ThreadItems.Children.Add(new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(48, 0, 0, 0),
                Children = { ChatViews.OwnerBubble(m.Text, Brush) },
            });
        // What tafs cloud didn't take, after it, where it was written.
        foreach (var m in coachUnsent)
            ThreadItems.Children.Add(ChatViews.UnsentBubble(App.Words, m, Brush, coachSending,
                () => RetryCoachUnsent(m), () => EditCoachUnsent(m), () => DeleteCoachUnsent(m),
                // Refused because the account is halted: Resume opens Halt all, Coach mode closing first.
                Unsent.ResumesWith(m, inTafChat: false) is HaltScreen.Account ? () => { Close(); ChatViews.ChooseResume(null, ""); } : null));
        Intro.Visibility = Visibility.Collapsed;
        ChatsView.Visibility = Visibility.Collapsed;
        ThreadView.Visibility = Visibility.Visible;
        ShowChatTools();
        if (thread?.Working is not null || coachSent.Count > 0) coachClock.Start(); else coachClock.Stop();
        if (toEnd) DispatcherQueue.TryEnqueue(() =>
        {
            ThreadScroll.UpdateLayout();
            ThreadScroll.ChangeView(null, ThreadScroll.ScrollableHeight, null, disableAnimation: true);
        });
    }

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
        var empty = conversation.Fresh && coachSent.Count == 0 && !coachSending;
        if (!empty)
        {
            conversationGen++;
            (conversation, session, thread, chatError, coachSending, drawnOrder) = (CoachConversation.New, null, null, null, false, null);
            coachSent.Clear();
            coachUnsent.Clear();
            answered.Clear();
            notes.Clear();
            cards.Clear();
            grants = null;
            drafts.Clear();
            draftErrors.Clear();
            ShowConversation();
        }
    }

    /// <summary>A conversation from the list, reopened: shown alone, and messages go on in it, as the iPad's <c>openSetupChat</c>.</summary>
    void OpenChat(string chosenSession)
    {
        conversationGen++;
        (conversation, session, thread, chatError, coachSending, drawnOrder) = (CoachConversation.Of(chosenSession), chosenSession, null, null, false, null);
        coachSent.Clear();
        coachUnsent.Clear();
        ChatsView.Visibility = Visibility.Collapsed;
        ThreadItems.Children.Clear();
        ThreadView.Visibility = Visibility.Visible;
        _ = LoadThreadAsync();
    }

    /// <summary>Ctrl+Enter sends; Enter alone is a new line, as a taf's chat.</summary>
    void OnCoachDraftKey(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (e.Key == Windows.System.VirtualKey.Enter && ctrl) { e.Handled = true; OnCoachSend(sender, e); }
    }

    /// <summary>
    /// Send, as the iPad's <c>deliverSetupOnce</c> without a stream: checked first (nothing to send says nothing; too long
    /// says so), shown at once, then sent into the conversation shown, or a new one. The conversation it went into is the
    /// one shown from then on. Not taken, it stays in the thread as not sent, with why, Edit and Retry (<see cref="Unsent"/>).
    /// </summary>
    async void OnCoachSend(object sender, RoutedEventArgs e)
    {
        var (text, why) = TafChatSend.Check(App.Words, CoachDraft.Text);
        if (text is null)
        {
            chatError = why;
            ShowChatTools();
            return;
        }
        // Out of the box at once, as it shows in the thread; not tried at all, back in the box unless something new is there.
        CoachDraft.Text = "";
        if (await SendCoachAsync(text) is null && CoachDraft.Text.Length == 0) CoachDraft.Text = text;
    }

    /// <summary>
    /// A message to Coach: null when it wasn't tried (already sending, or no Coach to send it to); true once sent; false
    /// when tafs cloud didn't take it, and the thread keeps it as not sent.
    /// </summary>
    async Task<bool?> SendCoachAsync(string text)
    {
        if (coachSending || coach is not { } id || answer is not { } send) return null;
        var w = App.Words;
        var (gen, mine) = (generation, conversationGen);
        var pending = TafChatSend.Pending(text, thread?.Messages ?? [], DateTimeOffset.UtcNow);
        coachSent.Add(pending);
        // The box is the Send button's to clear: Retry leaves what's being typed there alone, as the iPad's submitSetup.
        (coachSending, chatError) = (true, null);
        ShowConversation();
        var (into, refused) = await CoachSend.SendAsync(w, id, text, conversation, send);
        if (gen != generation || mine != conversationGen) return null;
        coachSending = false;
        if (refused is not null)
        {
            coachSent.Remove(pending);
            coachUnsent.Add(UnsentMessage.Of(text, refused.Why, DateTimeOffset.UtcNow, refused.Halted));
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

    /// <summary>Messages tafs cloud didn't take in the conversation shown, kept after it until Retry, Edit or Delete takes them.</summary>
    List<UnsentMessage> coachUnsent = [];

    /// <summary>Retry, as the iPad's <c>retrySetup</c>: sent again once; not tried at all, it goes back in its place.</summary>
    async void RetryCoachUnsent(UnsentMessage m)
    {
        if (coachSending) return;
        (coachUnsent, var at) = Unsent.Take(coachUnsent, m.Id);
        if (await SendCoachAsync(m.Text) is null) coachUnsent = Unsent.PutBack(coachUnsent, m, at);
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
        if (showingArchived) { _ = ShowChatsAsync(archived: false); return; }
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
        var (rows, unread) = await CoachChats.ReadAsync(w, archived ? CoachChats.ArchivedPath(id) : CoachChats.ListPath(id), reader);
        if (gen != generation || showingArchived != archived || ChatsView.Visibility != Visibility.Visible) return;
        ChatsItems.Children.Clear();
        if ((why ?? unread) is { } said) ChatsItems.Children.Add(Words(said, 13, "TafsNoText"));
        foreach (var row in rows ?? [])
            ChatsItems.Children.Add(ChatRow(id, row, archived));
        if (rows is { Count: 0 })
            ChatsItems.Children.Add(Words(w.Get(archived ? "coach.chats.nothing_archived" : "coach.chats.none"), 14, "TafsDimText", HorizontalAlignment.Center));
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
                if (why is null && !archived && row.Session == session)
                {
                    conversationGen++;
                    (conversation, session, thread, drawnOrder) = (CoachConversation.New, null, null, null);
                    coachSent.Clear();
                    coachUnsent.Clear();
                }
                await ShowChatsAsync(archived, why);
            };
            grid.Children.Add(act);
        }
        return grid;
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

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Home;
using Tafs.Core.Record;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// What a taf's page opens with: its head, its Card bar (none without a Card), its Activity for a filter, its Setup (none
/// yet from tafs cloud), and the tab it opens on. tafs cloud's comes in its own change; until then, the iPad's sample.
/// </summary>
/// A taf tafs cloud runs has its id there and the read its Activity comes through.
public sealed record TafOpening(TafHead Head, CardBarLine? Card, Func<string, ActivityPage> Activity, SetupOpening? Setup = null,
    TafTab OpensOn = TafTab.Activity, IReadOnlyList<TafCard>? Cards = null, string IssuedBy = "", string? CloudId = null,
    Func<string, Task<CloudRead>>? Read = null, Func<string, string?, Task<CloudRead>>? ReadWithProof = null,
    Func<Task<string?>>? SetupProof = null, bool Revoked = false, IReadOnlyList<Tafs.Core.Coach.ChatMessage>? SampleChat = null,
    TafHalt? Halt = null, Func<string, byte[], Task<CloudRead>>? Send = null, bool Manages = false, TafStream? Stream = null,
    TafRemove? Remove = null, Func<HttpMethod, string, byte[]?, Task<CloudRead>>? Change = null, TafStarts? Starts = null,
    bool Paused = false, string? Email = null, string? TimeZone = null, Action? AskCoach = null, SetupScreen SetupOn = SetupScreen.Main,
    TafHealth? Health = null, Action<string>? AskCoachWith = null, Action? OpenUsage = null, Func<bool>? MayUseCoach = null,
    CardScreen CardOn = CardScreen.Main, bool CreditsPrevious = false, TafTrying? Trying = null, TafInTrial? InTrial = null,
    TafInstalling? Installing = null, Func<int, KnobItem, Task<KnobEventResult>>? ApproveEvent = null, TafSharing? Share = null)
{
    /// <summary>
    /// Coach, improving this taf (the iPad's <c>improveTaf</c>), for someone who may use Coach as KIFF says when it's drawn:
    /// KIFF's answer can come after the page opened, so it's asked each time, never kept from the opening.
    /// </summary>
    public Action? Improve => MayUseCoach?.Invoke() == true ? AskCoach : null;
}

/// <summary>
/// Remove for good, for whoever manages the taf on a PC that signs with its passkey: what goes with it, in tafs cloud's words,
/// for the confirmation, and the removal itself (<see cref="TafRemoval.RemoveAsync"/>).
/// </summary>
public sealed record TafRemove(Func<Task<string?>> Goes, Func<Task<RemovalResult>> Run);

/// <summary>A POST to tafs cloud whose answer streams: its status, its body when refused, and the open answer when not.</summary>
public delegate Task<(int Status, string Body, System.Net.Http.HttpResponseMessage? Open)> TafStream(string path, byte[] body, CancellationToken cancel);

/// <summary>A taf's halt row, for whoever manages the taf: how its halt is read, and what opens its screen.</summary>
public sealed record TafHalt(Func<Task<Halt>> Read, Action Open);

/// <summary>The sample's Setup, as taf-setup.png: its job and the notes it keeps.</summary>
public sealed record SetupOpening(string Job, IReadOnlyList<MemoryNote> Notes);

/// <summary>One day of a taf's Activity, as drawn: tafs cloud sends a day going on from the page before with no title.</summary>
public sealed record TafDayView(string Title, IReadOnlyList<TafLineView> Lines) { public Visibility HasTitle => Title.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }

/// <summary>One line of a taf's Activity, as drawn: its dot, what it was, its time, the words under it, and any money it moved.</summary>
public sealed record TafLineView(string Title, string Time, string Detail, string Amount, Visibility HasAmount,
    Visibility IsDone, Visibility IsWaiting, Visibility IsRefused)
{
    public static TafLineView Of(ActivityLine l)
    {
        static Visibility Seen(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
        var dot = RecordScreen.DotOf(l.Kind);
        return new(l.Title, l.Time, l.Detail, l.Amount ?? "", Seen(l.Amount is not null),
            Seen(dot == Dot.Went), Seen(dot == Dot.Waiting), Seen(dot == Dot.Stopped));
    }
}

/// <summary>A taf's page, on its Chat, its Activity, its Card or its Setup.</summary>
public sealed partial class TafPage : Page
{
    TafOpening? opening;
    string filter = "all";
    /// <summary>tafs cloud's Activity, one set of pages a filter, as the iPad keeps them by taf and filter.</summary>
    readonly Dictionary<string, ActivityPages> cloudPages = [];
    /// <summary>tafs cloud's Setup for this taf, as last read.</summary>
    SetupRead setupRead = new();

    /// <summary>
    /// Each taf's Setup as last read in this session, by its id: opened again, its page draws at once and is read again
    /// underneath. Another session starts from nothing (<see cref="setupsFor"/> is that session's read).
    /// </summary>
    static readonly Tafs.Core.Kiff.SessionScoped<string, SetupRead> setups = new();

    /// <summary>Signed out: every taf's Setup kept goes, connections included.</summary>
    public static void ForgetSetups() => setups.Forget();
    /// <summary>Its thread, as tafs cloud last answered it, and why the last read didn't come (the iPad's chatErrors).</summary>
    Tafs.Core.Coach.CoachThread? chat;
    string? chatError;
    TafTab tab;
    List<MemoryNote> notes = [];
    bool paused;
    bool revoked;
    string? front;

    public TafPage()
    {
        InitializeComponent();
        // The thread read again every 2 s while the taf works or a sent message waits for it, only while Chat shows.
        chatClock = new DispatcherTimer { Interval = TafChatSend.WhileWorking };
        chatClock.Tick += (_, _) => { if (tab == TafTab.Chat) _ = LoadChatAsync(); };
        Unloaded += (_, _) => chatClock.Stop();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        opening = e.Parameter as TafOpening;
        if (opening is null) return;
        var head = opening.Head;
        var colour = RecordLineView.Colour(head.Colour);
        Avatar.Background = colour;
        Initial.Text = head.Initial;
        NameText.Text = head.Name;
        ShowShare();
        StateText.Text = head.State;
        WaitingText.Text = head.State;
        PausedText.Text = App.Words.Get("taf.state.paused");
        StoppedText.Text = App.Words.Get("taf.state.stopped");
        notes = [.. opening.Setup?.Notes ?? []];
        // A taf whose Card KIFF revoked opens revoked, as the iPad's taf.card.revoked: its Cards say Revoked and fade.
        revoked = opening.Revoked;
        // Paused, as Home says: its chip, and Resume every start on Setup › Starts when.
        paused = opening.Paused;
        liveResults = opening.Starts?.Results;
        tab = Built(opening.OpensOn) ? opening.OpensOn : TafTab.Activity;
        // A trial: its own chat only, under its bar where the tabs are.
        if (opening.InTrial is not null)
        {
            tab = TafTab.Chat;
            TabsTrack.Visibility = Visibility.Collapsed;
            TrialBar.Visibility = Visibility.Visible;
            ShowTrialBar();
            ShowState();
            ShowTab();
            _ = LoadChatAsync();
            return;
        }

        if (opening.Card is { } card)
        {
            SmallCard.Background = colour;
            (CardHero.Text, CardUnder.Text) = (card.Hero, card.Under);
            AutomationProperties.SetName(CardBar, $"{card.Hero}, {card.Under}");
            AutomationProperties.SetHelpText(CardBar, App.Words.Get("taf.shows_cards"));
        }
        ShowState();
        ShowTab();
        ActivityTryAgain.Content = App.Words.Get("common.try_again");
        Show();
        _ = LoadActivityAsync();
        _ = LoadSetupAsync();
        // Its work and its runs, above its record, for a taf tafs cloud runs.
        ShowWork();
        _ = LoadWorkAsync();
        if (tab == TafTab.Chat) _ = LoadChatAsync();
        if (opening.SetupOn != SetupScreen.Main) OpenSetupScreen(opening.SetupOn);
        if (opening.CardOn != CardScreen.Main) OpenCardScreen(opening.CardOn);
    }

    /// <summary>
    /// Its thread, read each time the Chat shows, as the iPad's <c>.task(id: tafID)</c> on its chat: a sample taf's is its
    /// greeting, kept here. A read that doesn't come keeps the thread there was and says why under it.
    /// </summary>
    async Task LoadChatAsync()
    {
        if (opening is not { CloudId: { } id, Read: { } read } mine) return;
        var (thread, error) = await TafChat.LoadAsync(App.Words, id, read, mine.InTrial is null ? null : TafTrials.ConversationPath(id));
        // An answer for a page since opened on another taf is dropped. A sign-out leaves for the sign-in page, and a taf's
        // page is never brought back from history, so an answer landing after it reaches a page nobody sees.
        if (opening != mine) return;
        if (thread is not null)
        {
            chat = thread;
            // What the person sent stays on screen until the thread has it, as the iPad's SentMessages.merge.
            sent = TafChatSend.Merge(thread.Messages, sent, DateTimeOffset.UtcNow).Waiting.ToList();
        }
        chatError = error;
        // While a run of its works, or a message waits for the thread, read again, as the iPad's chat does as it works.
        if (thread?.Working is not null || sent.Count > 0) chatClock.Start(); else chatClock.Stop();
        if (tab == TafTab.Chat) ShowChat();
    }

    /// <summary>
    /// The answer at work, as the iPad's <c>LiveWork</c>: the live line on top with its spinner, the steps under it (a failed
    /// one in red with why), and the words as they come.
    /// </summary>
    static StackPanel LiveBlock(LiveAnswer answer, Func<string, Microsoft.UI.Xaml.Media.Brush> brush)
    {
        var w = App.Words;
        var block = new StackPanel { Spacing = 6 };
        if (answer.Line(w) is { } line)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new ProgressRing { IsActive = true, Width = 14, Height = 14 });
            row.Children.Add(CoachPanel.Words(line, 13, "TafsSoftText"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(row, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            block.Children.Add(row);
        }
        // The steps done so far, as the iPad's LiveWork: tries of one step as one row, failures left out while it works.
        if (answer.DoneRows() is { Count: > 0 } rows) block.Children.Add(ChatViews.StepList(rows, brush));
        if (answer.Text.Length > 0) block.Children.Add(CoachPanel.Words(answer.Text, 16, "TafsInkText"));
        return block;
    }

    /// <summary>Messages sent here that the thread doesn't have yet, shown after it.</summary>
    List<Tafs.Core.Coach.ChatMessage> sent = [];
    bool sending;
    /// <summary>Why the last message or Stop didn't go through, under the thread; "You stopped Refunds." once stopped.</summary>
    string? chatNote;
    bool chatNoteIsError;
    DispatcherTimer chatClock = null!;

    /// <summary>
    /// The composer under the thread, for a taf tafs cloud runs and someone signed in: "Message Refunds", Send, and Stop
    /// while a run of its works for someone who manages it. A message being sent turns Send off, as the iPad's one at a time.
    /// </summary>
    void ShowComposer()
    {
        var w = App.Words;
        var writable = opening is { CloudId: not null, Send: not null };
        Composer.Visibility = tab == TafTab.Chat && writable ? Visibility.Visible : Visibility.Collapsed;
        if (!writable) return;
        var name = opening!.Head.Name;
        var working = chat?.Working is not null || live is { Ended: false };
        Draft.PlaceholderText = working ? w.Get("chat.send_while_works", ("taf", name)) : w.Get("chat.message_taf", ("taf", name));
        AutomationProperties.SetName(Draft, Draft.PlaceholderText);
        SendButton.Content = w.Get(sending ? "common.sending" : "common.send");
        ShowTray();
        // A trial's chat takes words only.
        AttachButton.Visibility = opening.InTrial is null ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Content = w.Get("chat.stop");
        StopButton.Visibility = working && opening.Manages ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Enter sends; Shift+Enter is a new line.</summary>
    void OnDraftKey(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Enter sends; Shift+Enter is a new line (seen before the box takes the Enter).
        if (ChatViews.SendsOnEnter(e)) { e.Handled = true; OnSend(sender, e); }
    }

    /// <summary>
    /// Send, as the iPad's <c>sendChat</c>: checked first (nothing to send says nothing; too long says so), shown at once,
    /// then sent. Not taken, it stays in the thread as not sent, with why, Edit and Retry (<see cref="Unsent"/>).
    /// </summary>
    async void OnSend(object sender, RoutedEventArgs e)
    {
        // Send waits while a file is going up; a message may be words, files, or both.
        if (FilesUploading) return;
        var (text, why) = TafChatSend.Check(App.Words, Draft.Text);
        if (text is null && Draft.Text.Trim().Length == 0 && ReadyFiles().Count > 0) (text, why) = ("", null);
        if (text is null)
        {
            (chatNote, chatNoteIsError) = (why, why is not null);
            ShowChat();
            return;
        }
        // Out of the box at once, as it shows in the thread; not tried at all, back in the box unless something new is there.
        Draft.Text = "";
        if (await SendTextAsync(text) is null && Draft.Text.Length == 0) Draft.Text = text;
    }

    /// <summary>
    /// A message to the taf: null when it wasn't tried (already sending, or nothing to send it to); true once sent; false
    /// when tafs cloud didn't take it, and the thread keeps it as not sent.
    /// </summary>
    async Task<bool?> SendTextAsync(string text)
    {
        // While it works, words alone go to the run at work, once (P34, the iPad's submitChat); files go on their own after.
        if (text.Trim().Length > 0 && ReadyFiles().Count == 0 && SteerRun() is { } run) return await SteerAsync(run, text);
        if (sending || opening is not { CloudId: { } id, Send: { } send } mine) return null;
        var w = App.Words;
        var pending = TafChatSend.Pending(text, chat?.Messages ?? [], DateTimeOffset.UtcNow);
        sent.Add(pending);
        // The box is the Send button's to clear: Retry leaves what's being typed there alone, as the iPad's submitChat.
        (sending, chatNote) = (true, null);
        ShowChat();
        // The files ready in the tray go with it; the tray empties once it's taken.
        var files = ReadyFiles();
        // A trial's message goes to its own chat, words only, never the live taf's.
        var refused = mine.InTrial is not null ? await TafChatSend.SendAsync(w, id, text, send, path: TafTrials.MessagesPath(id))
            : mine.Stream is { } stream ? await StreamAsync(w, id, text, stream, send, mine, files) : await TafChatSend.SendAsync(w, id, text, send, files);
        if (refused is null && files.Count > 0) ClearSentFiles(files);
        if (opening != mine) return null;
        (sending, live) = (false, null);
        if (refused is { Consent: true })
        {
            // Refused for want of the AI-data consent: its screen says why, and the words go back in the box, with no Not sent
            // row, to send once it's allowed, as the iPad's.
            sent.Remove(pending);
            ShowChat();
            return null;
        }
        if (refused is not null)
        {
            sent.Remove(pending);
            unsent.Add(UnsentMessage.Of(text, refused.Why, DateTimeOffset.UtcNow, refused.Halted));
            ShowChat();
            return false;
        }
        await LoadChatAsync();
        return true;
    }

    /// <summary>Messages tafs cloud didn't take, kept after the thread until Retry, Edit or Delete takes them.</summary>
    List<UnsentMessage> unsent = [];

    /// <summary>Retry, as the iPad's <c>retryChat</c>: sent again once; not tried at all, it goes back in its place.</summary>
    async void RetryUnsent(UnsentMessage m)
    {
        if (sending) return;
        (unsent, var at) = Unsent.Take(unsent, m.Id);
        if (await SendTextAsync(m.Text) is null) unsent = Unsent.PutBack(unsent, m, at);
        ShowChat();
    }

    /// <summary>
    /// The run a message written now goes to: the answer streaming here, else the thread's run at work. None for a trial's
    /// chat, or while the message that starts the run is still on its way (no run yet: it stays in the box).
    /// </summary>
    string? SteerRun() => opening is { InTrial: null, CloudId: not null, Send: not null } && (live?.Run ?? chat?.WorkingRun) is { Length: > 0 } run
        && (sending || chat?.WorkingRun is not null) ? run : null;

    /// <summary>
    /// Words to the run at work, as the iPad's <c>submitChat</c>: taken, or kept by tafs cloud (sent at once, or held until
    /// the taf is done), then the thread is read again, which shows where they are. More than tafs cloud holds: its words, and
    /// they stay in the box. Not reached: kept in the thread as not sent, with Retry; never sent again on its own.
    /// </summary>
    async Task<bool?> SteerAsync(string run, string text)
    {
        if (opening is not { CloudId: { } id, Send: { } send } mine) return null;
        var w = App.Words;
        chatNote = null;
        var steered = await TafSteer.SteerAsync(w, id, run, text, send);
        if (opening != mine) return null;
        switch (steered)
        {
            case Steered.Full full:
                (chatNote, chatNoteIsError) = (full.Why, true);
                ShowChat();
                return null;
            case Steered.Refused { Why.Consent: true }:
                ShowChat();
                return null;
            case Steered.Refused refused:
                unsent.Add(UnsentMessage.Of(text, refused.Why.Why, DateTimeOffset.UtcNow, refused.Why.Halted));
                ShowChat();
                return false;
            default:
                // Shown where the thread puts it once read: untaken under the run, or held for after.
                if (steered is Steered.Taken) steeredNow.Add(new SteerMessage(text, DateTimeOffset.UtcNow));
                ShowChat();
                await LoadChatAsync();
                return true;
        }
    }

    /// <summary>What this page wrote to the run that the thread doesn't show yet: shown at once, as the iPad's.</summary>
    readonly List<SteerMessage> steeredNow = [];

    /// <summary>
    /// What the thread says of messages written while it works (the iPad's steering rows): each the run hasn't read, on the
    /// right, with "Refunds reads this at its next step" (or "…when it carries on"); what waits until it's done, with "Waits
    /// until it's done, then goes as your next message"; what a stopped run never read, with tafs cloud's why.
    /// </summary>
    void AddSteering(Func<string, Microsoft.UI.Xaml.Media.Brush> brush)
    {
        var w = App.Words;
        var name = opening?.Head.Name ?? "";
        var steering = chat?.Steering;
        // Shown at once until the thread has it: then the thread's own.
        // Gone too once the run has read it: the thread then holds it as the person's own message, written at or after it.
        steeredNow.RemoveAll(s => steering?.Untaken.Any(u => u.Text == s.Text) == true || chat?.WorkingRun is null && !sending
            || chat?.Messages.Any(m => m.From == Tafs.Core.Coach.ChatFrom.Owner && m.Text == s.Text && m.At >= s.At - TimeSpan.FromSeconds(5)) == true);
        void Row(string text, string line, bool error = false)
        {
            var said = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(48, 0, 0, 0) };
            said.Children.Add(ChatViews.OwnerBubble(text, brush));
            var under = CoachPanel.Words(line, 12, error ? "TafsNoText" : "TafsDimText", HorizontalAlignment.Right);
            said.Children.Add(under);
            ChatPane.Children.Add(said);
        }
        foreach (var m in (steering?.Untaken ?? []).Concat(steeredNow))
            Row(m.Text, TafSteer.ReadsLine(w, name, steering?.Paused == true));
        if (steering?.Held is { } held) Row(held.Text, w.Get("chat.held"));
        foreach (var m in steering?.NotSent ?? []) Row(m.Text, m.Why, error: true);
    }

    /// <summary>Edit, as the iPad's <c>editUnsentChat</c>: out of the thread, its words in the box to change.</summary>
    void EditUnsent(UnsentMessage m)
    {
        unsent = Unsent.Take(unsent, m.Id).Left;
        Draft.Text = m.Text;
        Draft.Focus(FocusState.Programmatic);
        Draft.SelectionStart = Draft.Text.Length;
        ShowChat();
    }

    void DeleteUnsent(UnsentMessage m)
    {
        unsent = Unsent.Take(unsent, m.Id).Left;
        ShowChat();
    }

    /// <summary>The answer as it comes, drawn under the sent message until the thread has it; none when nothing streams.</summary>
    LiveAnswer? live;

    /// <summary>
    /// The message sent with its answer streamed (<c>/messages/stream</c>), as the iPad's send: each event drawn as it comes,
    /// then the thread read again. The stream not opening (not reached) sends it plainly, as the iPad's; a refusal is said,
    /// never sent again. A stream that breaks leaves the rest to the thread.
    /// </summary>
    async Task<Refusal?> StreamAsync(Tafs.Core.Words.WordBook w, string id, string text, TafStream stream,
        Func<string, byte[], Task<CloudRead>> send, TafOpening mine, IReadOnlyList<string>? files = null)
    {
        (int Status, string Body, System.Net.Http.HttpResponseMessage? Open) answer;
        try { answer = await stream(TafChatStream.Path(id), TafChatSend.Body(text, files), CancellationToken.None); }
        catch (System.Net.Http.HttpRequestException) { return await TafChatSend.SendAsync(w, id, text, send, files); }
        // No stream route there (404, 405): the plain send takes it, as the iPad's (an older tafs cloud, or the stream's host).
        if (answer.Open is null && answer.Status is 404 or 405) return await TafChatSend.SendAsync(w, id, text, send, files);
        if (answer.Open is not { } open)
            return Refusal.Of(CloudRead.Refused(w, answer.Status, answer.Body), w.Get("error.ours"));
        using (open)
        {
            live = LiveAnswer.Start;
            ShowChat();
            try
            {
                using var reader = new StreamReader(await open.Content.ReadAsStreamAsync());
                await foreach (var e in ServerSentEvents.ReadAsync(reader))
                {
                    if (opening != mine) return null;
                    live = live.Apply(e);
                    // The run read a message written meanwhile: the thread has it.
                    if (e.Name == "taken") _ = LoadChatAsync();
                    if (tab == TafTab.Chat) ShowChat();
                    if (live.Ended) break;
                }
            }
            catch (Exception e) when (e is IOException or System.Net.Http.HttpRequestException) { }
        }
        // How it ended, in the words the run stopped with: KIFF's or the AI provider's, whole, as the apps show them.
        // tafs cloud sends words only with a stop code (kiff/agents' words for people), so their presence is the signal.
        if (live is { EndWords: { } why }) (chatNote, chatNoteIsError) = (why, true);
        return null;
    }

    /// <summary>Stop, as the iPad's <c>stopChat</c>: the run at work is cancelled; "You stopped Refunds.", or why not.</summary>
    async void OnStop(object sender, RoutedEventArgs e)
    {
        if (opening is not { CloudId: { } id, Send: { } send, Manages: true } mine || (live?.Session ?? chat?.Working) is not { } session) return;
        StopButton.IsEnabled = false;
        var why = await TafChatSend.StopAsync(App.Words, id, session, send);
        if (opening != mine) return;
        StopButton.IsEnabled = true;
        (chatNote, chatNoteIsError) = why is null ? (App.Words.Get("chat.you_stopped", ("taf", mine.Head.Name)), false) : (why, true);
        await LoadChatAsync();
    }

    /// <summary>
    /// The thread, as the iPad's <c>TafChatView.thread</c>: a time between parts an hour apart; the person's words on the right
    /// in a brand bubble, the files sent with them above, "Answered the card above" under an answer; the taf's full width, with
    /// "Worked for 12 s" above them, which opens its steps, where its result went above and what it did under, and a refused
    /// connection's lines; a run's start as a line. A message through a door has its stamp: over it, where it came from;
    /// under the taf's answer, how it went back. With nothing in it, what the chat is for; a read that didn't come, why, in red.
    /// </summary>
    /// <summary>
    /// A taf's answer's menu, as Coach's and the iPad's <c>reportable</c>: Report this answer, opening the report. It goes to
    /// tafs cloud against this taf (a trial's answers too), with the message's session, as the iPad's <c>reportAnswer</c>.
    /// </summary>
    MenuFlyout ReportMenu(Tafs.Core.Coach.ChatMessage m)
    {
        var w = App.Words;
        var item = new MenuFlyoutItem { Text = w.Get("report.menu"), Icon = new FontIcon { Glyph = "" } };
        item.Click += async (_, _) =>
        {
            if (opening?.CloudId is not { } taf || opening.Send is not { } send) return;
            await new ReportAnswerDialog(XamlRoot, (reason, note) =>
                Tafs.Core.Coach.AnswerReport.SendAsync(w, async (path, body) => await send(path, body), taf, m, reason, note)).ShowAsync();
        };
        return new MenuFlyout { Items = { item } };
    }

    void ShowChat()
    {
        var w = App.Words;
        // The latest message in view, as the iPad's thread opens at its end: once the thread is laid out, unless the person
        // had scrolled up to read.
        var atEnd = PageScroll.VerticalOffset >= PageScroll.ScrollableHeight - 24;
        if (atEnd) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => PageScroll.ChangeView(null, PageScroll.ScrollableHeight, null, disableAnimation: true));
        ChatPane.Children.Clear();
        Microsoft.UI.Xaml.Media.Brush Brush(string key) => ChatViews.ThemeBrush(ActualTheme, key);
        IReadOnlyList<Tafs.Core.Coach.ThreadItem> items = chat is { } thread ? TafChat.Items(thread)
            : (opening?.SampleChat ?? []).Select(m => new Tafs.Core.Coach.ThreadItem(m, null)).ToList();
        // What the person sent that the thread doesn't have yet, after it, in the order sent.
        if (sent.Count > 0) items = [.. items, .. sent.Select(m => new Tafs.Core.Coach.ThreadItem(m, null))];
        // Before a trial's first message, what it is and what to try.
        if (items.Count == 0 && unsent.Count == 0 && opening?.InTrial is { } trial) AddTrialGuide(trial);
        else if (items.Count == 0 && unsent.Count == 0)
        {
            var empty = CoachPanel.Words(TafChat.Empty(w, opening?.Head.Name), 14, "TafsDimText", HorizontalAlignment.Center);
            empty.Margin = new Thickness(0, 30, 0, 30);
            ChatPane.Children.Add(empty);
        }
        for (var i = 0; i < items.Count; i++)
        {
            if (Tafs.Core.Coach.CoachChat.GapTime(w, items, i, DateTimeOffset.Now, TimeZoneInfo.Local) is { } gap)
            {
                var time = CoachPanel.Words(gap, 12, "TafsDimText", HorizontalAlignment.Center);
                time.Margin = new Thickness(0, 6, 0, 0);
                ChatPane.Children.Add(time);
            }
            var m = items[i].Message!;
            if (m is { From: Tafs.Core.Coach.ChatFrom.Owner, Kickoff: false })
            {
                var said = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(48, 0, 0, 0) };
                // Through a door, someone else's words: where they came from, over them.
                if (m.Door is { } door) said.Children.Add(ChatViews.DoorStamp(door, fromOwner: true, Brush));
                // Each opens again, full size, in Windows' own app for its kind.
                if (m.Files is { } files) said.Children.Add(ChatViews.MessageFiles(files, Brush, file => _ = OpenFileAsync(file)));
                if (m.Text.Length > 0) said.Children.Add(ChatViews.OwnerBubble(m.Text, Brush));
                // Written while a card waited: these words were its answer. Not a door's.
                if (m is { Answers: not null, Door: null }) said.Children.Add(ChatViews.AnsweredCardLine());
                if (said.Children.Count > 0) ChatPane.Children.Add(said);
                continue;
            }
            var lines = new StackPanel { Spacing = 6 };
            // What it did before writing it, folded.
            if (m is { From: Tafs.Core.Coach.ChatFrom.Taf, Work: { } work })
                lines.Children.Add(ChatViews.WorkSummary(TafChat.WorkRows(w, work), work.Ms, Brush));
            // Where the run's result went: tafs cloud's line, above the taf's words; none on a run's start.
            if (m.RunLines.Above is { } delivered) lines.Children.Add(ChatViews.RunLine(delivered));
            if (m.Kickoff) lines.Children.Add(ChatViews.Kickoff(m));
            else
            {
                var text = CoachPanel.Words(m.Text, 16, "TafsInkText");
                text.IsTextSelectionEnabled = true;
                // Its answers can be reported, as Coach's (the iPad's reportable, for a taf's chat and a trial's).
                if (Tafs.Core.Coach.AnswerReport.Can(m, kiff: opening?.Send is not null && opening.CloudId is not null)) text.ContextFlyout = ReportMenu(m);
                lines.Children.Add(text);
                // Its answer to a door's message: how it went back, under it.
                if (m.Door is { } door) lines.Children.Add(ChatViews.DoorStamp(door, fromOwner: false, Brush));
            }
            // What the run did, counted from the record: tafs cloud's line, under them.
            if (m.RunLines.Under is { } counted) lines.Children.Add(ChatViews.RunLine(counted));
            // A connection its work asked for that was refused: tafs cloud's lines, never folded.
            if (m.RefusedLines is { } refused) lines.Children.Add(ChatViews.RefusedLines(refused, Brush));
            ChatPane.Children.Add(lines);
        }
        // Written while it works: what the run hasn't read, what waits for after, what a stopped run never read.
        AddSteering(Brush);
        // What tafs cloud didn't take, after the thread, where it was written.
        foreach (var m in unsent)
            ChatPane.Children.Add(ChatViews.UnsentBubble(w, m, Brush, sending, () => RetryUnsent(m), () => EditUnsent(m), () => DeleteUnsent(m),
                // Refused for a halt: Resume opens the Halt screen that resumes it, the account's or this taf's.
                Unsent.ResumesWith(m, inTafChat: true) switch
                {
                    HaltScreen.Account => () => ChatViews.ChooseResume(null, ""),
                    HaltScreen.Taf when opening?.CloudId is { } taf => () => ChatViews.ChooseResume(taf, opening.Head.Name),
                    _ => null,
                }));
        if (chatError is { } why)
        {
            var error = CoachPanel.Words(why, 13, "TafsSoftText");
            error.Foreground = Brush("TafsNo");
            ChatPane.Children.Add(error);
        }
        if (live is { } answer && !answer.Ended) ChatPane.Children.Add(LiveBlock(answer, Brush));
        if (chatNote is { } note)
        {
            var line = CoachPanel.Words(note, 13, "TafsSoftText");
            if (chatNoteIsError) line.Foreground = Brush("TafsNo");
            ChatPane.Children.Add(line);
        }
        ShowComposer();
    }

    /// <summary>Its Setup, read as the page opens and on Try again, as the iPad's <c>.task(id: tafID)</c> on its page.</summary>
    async Task LoadSetupAsync()
    {
        if (opening is not { CloudId: { } id, Read: { } read }) return;
        var kept = setups.Get(read, id, () => new SetupRead());
        setupRead = kept;
        if (tab == TafTab.Setup && kept.Page is not null) ShowSetup();
        // Drawn as soon as the quick read lands; the connections when planque's signed read does.
        await kept.LoadAsync(id, opening.ReadWithProof ?? ((path, _) => read(path)), opening.SetupProof,
            () => { if (tab == TafTab.Setup && opening?.CloudId == id) ShowSetup(); });
        if (tab == TafTab.Setup && opening?.CloudId == id) ShowSetup();
    }

    /// <summary>
    /// The pages for the filter and selection, as the iPad's <c>activityKey(id, filter, selection)</c>. No screen gives a
    /// selection yet (Setup's "last 7 days" will); until then it's always none.
    /// </summary>
    const string Selection = "";

    ActivityPages CloudPages() => cloudPages.TryGetValue($"{filter}:{Selection}", out var p) ? p : cloudPages[$"{filter}:{Selection}"] = new();

    /// <summary>
    /// The filter's newest page, or with <paramref name="more"/> its next, drawn as the read starts and once it's in, as the
    /// iPad's <c>loadActivityPage</c> for the filter chosen. A sample taf has none to read.
    /// </summary>
    async Task LoadActivityAsync(bool more = false)
    {
        if (opening is not { CloudId: { } id, Read: { } read }) return;
        var pages = CloudPages();
        var loading = pages.LoadAsync(id, filter, read, more, Selection);
        Show();
        await loading;
        Show();
    }

    /// <summary>Try again: the next page when some are in, else the newest, as the iPad's.</summary>
    void OnActivityTryAgain(object sender, RoutedEventArgs e) => _ = LoadActivityAsync(more: CloudPages().Pages.Count > 0);

    /// <summary>The next page is read once its ring comes into view, once for each cursor, as the iPad's <c>.task(id: cursor)</c>.</summary>
    async void OnActivityLoadingSeen(FrameworkElement sender, EffectiveViewportChangedEventArgs e)
    {
        var inView = e.EffectiveViewport.Height > 0 && e.BringIntoViewDistanceY <= 0;
        if (!inView || opening is not { CloudId: { } id, Read: { } read }) return;
        var seen = CloudPages().SeenAsync(id, filter, read, Selection);
        Show();
        await seen;
        Show();
    }

    /// <summary>
    /// The tabs, the chosen one filled, and its pane: each tab the taf has opens (<see cref="Built"/>), and one it hasn't is
    /// shown off, so the page keeps the iPad's tabs. The Card bar is above the Chat and the Activity, as the iPad's.
    /// </summary>
    void ShowTab()
    {
        var w = App.Words;
        Tabs.Children.Clear();
        Tabs.ColumnDefinitions.Clear();
        AutomationProperties.SetName(Tabs, TafScreen.TabsName(w));
        var tabs = TafScreen.Tabs(w);
        for (var i = 0; i < tabs.Count; i++)
        {
            var (each, title) = tabs[i];
            Tabs.ColumnDefinitions.Add(new ColumnDefinition());
            var b = new Button { Content = title, Style = Look(each == tab ? "TafsSegmentChosen" : "TafsSegment") };
            if (each == tab) AutomationProperties.SetItemStatus(b, "selected");
            if (Built(each))
                b.Click += (_, _) =>
                {
                    tab = each;
                    ShowTab();
                    if (each == TafTab.Chat) _ = LoadChatAsync();
                };
            else
            {
                b.IsEnabled = false;
                b.Opacity = 0.45;
            }
            Grid.SetColumn(b, i);
            Tabs.Children.Add(b);
        }
        ActivityPane.Visibility = tab == TafTab.Activity ? Visibility.Visible : Visibility.Collapsed;
        SetupPane.Visibility = tab == TafTab.Setup ? Visibility.Visible : Visibility.Collapsed;
        CardPane.Visibility = tab == TafTab.Card ? Visibility.Visible : Visibility.Collapsed;
        ChatPane.Visibility = tab == TafTab.Chat ? Visibility.Visible : Visibility.Collapsed;
        ShowComposer();
        // Over the Chat and the Activity, as the iPad's (section < 2).
        CardBar.Visibility = tab is TafTab.Chat or TafTab.Activity && opening?.Card is not null ? Visibility.Visible : Visibility.Collapsed;
        if (tab == TafTab.Setup) ShowSetup();
        if (tab == TafTab.Card) ShowCards();
        if (tab == TafTab.Chat) ShowChat();
    }

    /// <summary>
    /// Setup, as taf-setup.png: the job; Starts when and Results go to, shown and off until their screens are built; what
    /// it remembers, each note with Forget; and Pause.
    /// </summary>
    void ShowSetup()
    {
        if (opening?.Setup is not { } setup) return;
        var w = App.Words;
        SetupPane.Children.Clear();
        if (opening.CloudId is not null)
        {
            if (setupScreen == SetupScreen.StartsWhen) ShowStartsWhen();
            else if (setupScreen == SetupScreen.ResultsGoTo) ShowResultsGoTo();
            else if (setupScreen == SetupScreen.Door) ShowDoor();
            else ShowCloudSetup(setup);
            return;
        }
        SetupPane.Children.Add(Panel(Eyebrow("setup.job"), Words(setup.Job, 15, "TafsInkText")));
        foreach (var (key, glyph) in new[] { ("setup.starts_when", ""), ("setup.results_go_to", "") })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15, Style = Look("TafsAmberIcon") });
            row.Children.Add(new TextBlock { Text = w.Get(key), FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = Look("TafsAmberWords") });
            var link = Panel(row);
            link.Opacity = 0.45;
            SetupPane.Children.Add(link);
        }

        var memory = new StackPanel { Spacing = 12 };
        memory.Children.Add(Eyebrow("taf.memory.title"));
        if (notes.Count == 0) memory.Children.Add(Words(w.Get("taf.memory.empty"), 14, "TafsSoftText"));
        foreach (var note in notes)
        {
            var line = new Grid { ColumnSpacing = 10 };
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var said = new StackPanel { Spacing = 3 };
            said.Children.Add(Words(note.Content, 15, "TafsInkText"));
            said.Children.Add(Words(TafSetup.NoteLine(note, TimeZoneInfo.Local), 12, "TafsSoftText"));
            var forget = new Button { Content = w.Get("taf.memory.forget"), Style = Look("TafsQuietButton"), VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetName(forget, w.Get("taf.memory.forget_label", ("note", note.Content)));
            forget.Click += async (_, _) => await ForgetAsync(note);
            Grid.SetColumn(forget, 1);
            line.Children.Add(said);
            line.Children.Add(forget);
            memory.Children.Add(line);
        }
        memory.Children.Add(Words(w.Get("taf.memory.explains"), 12, "TafsSoftText"));
        SetupPane.Children.Add(Panel(memory));

        var pause = new Button { Content = w.Get(paused ? "taf.resume" : "taf.pause"), Style = Look("TafsOutlineButton"), HorizontalAlignment = HorizontalAlignment.Stretch };
        pause.Click += (_, _) =>
        {
            paused = !paused;
            ShowState();
            ShowSetup();
        };
        // Once its Card is revoked, nothing of it runs: nothing to pause.
        if (!revoked) SetupPane.Children.Add(pause);
    }

    /// <summary>
    /// A taf from tafs cloud's Setup, as the iPad's <c>TafDetailView.setup</c> in its order: the Card-unit exceptions, its job,
    /// its connected apps around it with its workflow, and what it remembers. What acts (Coach, signing, signing in again,
    /// See all, Remove) comes with its own change.
    /// </summary>
    void ShowCloudSetup(SetupOpening setup)
    {
        var page = setupRead.Page;
        foreach (var ask in page?.CardUnitExceptions ?? [])
        {
            var title = Words(ask.Title, 16, "TafsInkText");
            title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            SetupPane.Children.Add(Panel(title, Words(ask.Words, 14, "TafsSoftText"), Words(ask.History, 12, "TafsDimText")));
        }
        // Its job, and Improve in Coach for whoever manages it, as the iPad's: Coach starts on improving this taf.
        var job = Panel(Eyebrow("setup.job"), Words(setup.Job, 15, "TafsInkText"));
        if (opening?.Manages == true && opening.Improve is { } improve)
        {
            var link = new HyperlinkButton { Content = App.Words.Get("setup.improve_in_coach"), Padding = new Thickness(0), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(link, "setup.improve");
            link.Click += (_, _) => improve();
            ((StackPanel)job.Child).Children.Add(link);
        }
        SetupPane.Children.Add(job);
        AddSetupLinks();

        // ConnectedToolsRing: why its connections couldn't be read, with Try again; a read going; else the ring and workflow.
        var tools = new StackPanel { Spacing = 10 };
        tools.Children.Add(Eyebrow("connect.connected_tools"));
        if (!setupRead.ConnectionsPending && (setupRead.Error ?? page?.ConnectionsError) is { } error)
        {
            tools.Children.Add(Words(error, 13, "TafsNoText"));
            var again = new Button { Content = App.Words.Get("common.try_again") };
            again.Click += (_, _) => _ = LoadSetupAsync();
            tools.Children.Add(again);
        }
        // Reading: the whole page, or its connections while planque's signed read is on its way.
        if ((page is null && setupRead.Error is null) || (setupRead.ConnectionsPending && page?.Connections.Count == 0))
            tools.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
        if (page is not null)
        {
            tools.Children.Add(Ring(TafSetup.Nodes(page.Connections), setup.Job));
            tools.Children.Add(Workflow(page.Workflow));
        }
        SetupPane.Children.Add(Panel(tools));

        // MemorySummaryPanel: how many notes and the latest, once read.
        var memory = new StackPanel { Spacing = 6 };
        memory.Children.Add(Eyebrow("taf.memory.title"));
        if (page?.Memory is { } m)
        {
            var count = Words(m.Title, 15, "TafsInkText");
            count.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            memory.Children.Add(count);
            if (m.Latest is { } latest)
            {
                var line = Words(latest, 14, "TafsSoftText");
                line.MaxLines = 2;
                line.TextTrimming = TextTrimming.CharacterEllipsis;
                memory.Children.Add(line);
            }
        }
        else memory.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
        SetupPane.Children.Add(Panel(memory));

        // Remove for good, last on Setup, in red, as the iPad's: what goes with it first, then Windows Hello. A revoked taf too.
        if (opening?.Remove is { } remove) SetupPane.Children.Add(RemoveButton(remove));
    }

    /// <summary>
    /// The iPad's ring, 280 high: each app on a circle around the taf, joined to it by a dashed line, its name and its
    /// actions' words; a running one with a green arc and its words green. The taf in the middle shows its job.
    /// </summary>
    Canvas Ring(IReadOnlyList<SetupNode> nodes, string job)
    {
        var canvas = new Canvas { Height = 280 };
        canvas.SizeChanged += (_, e) => DrawRing(canvas, nodes, job, e.NewSize.Width);
        return canvas;
    }

    void DrawRing(Canvas canvas, IReadOnlyList<SetupNode> nodes, string job, double width)
    {
        canvas.Children.Clear();
        if (opening is null || width <= 0) return;
        var head = opening.Head;
        var (cx, cy) = (width / 2, 140.0);
        var radius = Math.Min(width, 280) / 2 - 36;
        var line = new Microsoft.UI.Xaml.Media.SolidColorBrush(ThemeColour("TafsLine"));
        var ok = new Microsoft.UI.Xaml.Media.SolidColorBrush(ThemeColour("TafsOk"));
        for (var i = 0; i < nodes.Count; i++)
        {
            var app = nodes[i].App;
            var angle = -Math.PI / 2 + i * 2 * Math.PI / Math.Max(nodes.Count, 1);
            var (x, y) = (cx + Math.Cos(angle) * radius, cy + Math.Sin(angle) * radius);
            canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Line
            {
                X1 = cx, Y1 = cy, X2 = x, Y2 = y, Stroke = line, StrokeThickness = 1,
                StrokeDashArray = new Microsoft.UI.Xaml.Media.DoubleCollection { 4, 4 },
            });
            if (app.Running)
            {
                // A tenth of the circle, from the app round, as the iPad's trimmed circle turned to its angle.
                var end = angle + 0.2 * Math.PI;
                var arc = new Microsoft.UI.Xaml.Media.PathFigure { StartPoint = new(x, y) };
                arc.Segments.Add(new Microsoft.UI.Xaml.Media.ArcSegment
                {
                    Point = new(cx + Math.Cos(end) * radius, cy + Math.Sin(end) * radius), Size = new(radius, radius),
                    SweepDirection = Microsoft.UI.Xaml.Media.SweepDirection.Clockwise,
                });
                var geometry = new Microsoft.UI.Xaml.Media.PathGeometry();
                geometry.Figures.Add(arc);
                canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
                {
                    Data = geometry, Stroke = ok, StrokeThickness = 3,
                    StrokeStartLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round, StrokeEndLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round,
                });
            }
            var name = new TextBlock { Text = app.Name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, MaxLines = 2,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Style = Look("TafsInkText") };
            var words = new TextBlock { Text = app.ActionWords, FontSize = 11, MaxLines = 2, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, Style = Look("TafsSoftText") };
            if (app.Running) words.Foreground = ok;
            var box = new StackPanel { Spacing = 4, Width = 78 };
            box.Children.Add(name);
            box.Children.Add(words);
            var node = new Border { Child = box, Padding = new Thickness(5), CornerRadius = new CornerRadius(14),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ThemeColour("TafsPanel")) };
            AutomationProperties.SetName(node, $"{app.Name}, {app.ActionWords}");
            node.Loaded += (_, _) =>
            {
                Canvas.SetLeft(node, x - node.ActualWidth / 2);
                Canvas.SetTop(node, y - node.ActualHeight / 2);
            };
            canvas.Children.Add(node);
        }
        var w = App.Words;
        var taf = new Button
        {
            Width = 54, Height = 54, Padding = new Thickness(0), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(0),
            Background = RecordLineView.Colour(head.Colour),
            Content = new TextBlock { Text = head.Initial, FontSize = 27, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) },
        };
        AutomationProperties.SetName(taf, w.Get("taf.job_label", ("taf", head.Name)));
        taf.Click += async (_, _) => await new ContentDialog
        {
            XamlRoot = XamlRoot, Title = head.Name, Content = job, CloseButtonText = w.Get("common.done"), RequestedTheme = ActualTheme,
        }.ShowAsync();
        Canvas.SetLeft(taf, cx - 27);
        Canvas.SetTop(taf, cy - 27);
        canvas.Children.Add(taf);
    }

    /// <summary>
    /// The workflow, folded as the iPad's DisclosureGroup: its title and summary, then each step's number (green while it
    /// runs), label, words and app.
    /// </summary>
    Expander Workflow(SetupWorkflow workflow)
    {
        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(Words(workflow.Title, 17, "TafsInkText")); // the DisclosureGroup label's default body size
        header.Children.Add(Words(workflow.Summary, 13, "TafsSoftText"));
        var steps = new StackPanel { Spacing = 8 };
        foreach (var step in workflow.Steps)
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var number = new TextBlock { Text = step.Number.ToString(System.Globalization.CultureInfo.CurrentCulture), FontSize = 15, Style = Look("TafsSoftText") };
            if (step.Running) number.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(ThemeColour("TafsOk"));
            row.Children.Add(number);
            var said = new StackPanel { Spacing = 4 };
            var label = Words(step.Label, 15, "TafsInkText");
            label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            said.Children.Add(label);
            if (step.Words.Length > 0) said.Children.Add(Words(step.Words, 13, "TafsSoftText"));
            if (step.App is { } app) said.Children.Add(Words(app, 12, "TafsAmberText"));
            Grid.SetColumn(said, 1);
            row.Children.Add(said);
            steps.Children.Add(row);
        }
        return new Expander { Header = header, Content = steps, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }

    /// <summary>Forget asks first, as the iPad's: it can't be undone. The sample's note goes from this page.</summary>
    async Task ForgetAsync(MemoryNote note)
    {
        var w = App.Words;
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = w.Get("taf.memory.forget_title"),
            Content = w.Get("taf.memory.forget_body", ("note", note.Content)),
            PrimaryButtonText = w.Get("taf.memory.forget"),
            CloseButtonText = w.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ActualTheme,
        };
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
        notes.Remove(note);
        ShowSetup();
    }

    /// <summary>
    /// Activity is always built; Setup when the taf has it; the Card with Cards, or a halt row; Chat, as the iPad's
    /// <c>canChat</c>, for a sample taf and for one tafs cloud runs.
    /// </summary>
    bool Built(TafTab each) => each switch
    {
        _ when opening?.InTrial is not null => each == TafTab.Chat,
        TafTab.Activity => true,
        TafTab.Chat => opening is { CloudId: not null, Read: not null } || opening?.SampleChat is not null,
        TafTab.Setup => opening?.Setup is not null,
        TafTab.Card => opening?.Cards is { Count: > 0 } || opening?.Halt is not null,
        _ => false,
    };

    void OnCardBar(object sender, RoutedEventArgs e)
    {
        if (!Built(TafTab.Card)) return;
        tab = TafTab.Card;
        ShowTab();
    }

    /// <summary>The head's chip: Working in green, Waiting amber, Paused soft, Stopped red once its Card is revoked.</summary>
    void ShowState()
    {
        var waiting = opening?.Head.Waiting == true;
        // A trial says it isn't live in its bar, never the live taf's state.
        if (opening?.InTrial is not null)
        {
            WorkingChip.Visibility = WaitingChip.Visibility = PausedChip.Visibility = StoppedChip.Visibility = Visibility.Collapsed;
            return;
        }
        WorkingChip.Visibility = !paused && !revoked && !waiting ? Visibility.Visible : Visibility.Collapsed;
        WaitingChip.Visibility = !paused && !revoked && waiting ? Visibility.Visible : Visibility.Collapsed;
        PausedChip.Visibility = paused && !revoked ? Visibility.Visible : Visibility.Collapsed;
        StoppedChip.Visibility = revoked ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// A Card behind the front one shows this much of its top edge, as in a wallet: 2 more than the iPad's 26, so its
    /// 12 mono name isn't cut at its foot in Segoe's line height.
    /// </summary>
    const double Edge = 28;

    /// <summary>
    /// The Card tab, as taf-card.png: its Cards stacked as in a wallet, the one in front whole and each behind a band of
    /// its top edge, a shade darker the further back (the furthest on top). A tap brings one to the front. Under them, every
    /// rule of the front Card with its limits, then Revoke.
    /// </summary>
    void ShowCards()
    {
        // Its versions or its usage, each on a screen of its own with Back.
        if (ShowCardScreen()) return;
        if (opening?.Cards is not { Count: > 0 } cards)
        {
            // A tafs cloud taf's Cards aren't drawn here yet: its halt row still is, for whoever manages it, and its rows.
            CardPane.Children.Clear();
            if (opening?.Halt is { } alone && !revoked) CardPane.Children.Add(HaltRow(alone));
            AddCardRows();
            return;
        }
        var w = App.Words;
        CardPane.Children.Clear();
        var shown = cards.FirstOrDefault(c => c.Id == front) ?? cards[0];
        var ordered = cards.Where(c => c.Id != shown.Id).Reverse().Append(shown).ToList();
        var stack = new Grid { Width = CardWidth, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < ordered.Count; i++)
        {
            var card = ordered[i];
            var depth = ordered.Count - 1 - i;
            var face = new Button { Style = Look("TafsBox"), Margin = new Thickness(0, i * Edge, 0, 0), VerticalAlignment = VerticalAlignment.Top,
                Content = Face(card, depth) };
            AutomationProperties.SetName(face, depth == 0 ? card.Spoken(w, opening.IssuedBy, revoked) : w.Get("taf.show_card", ("title", card.Title)));
            face.Click += (_, _) => { front = card.Id; ShowCards(); };
            // Each Card casts the iPad's shadow up onto the one behind (black at 16%, radius 4, 2 up), so the bands read apart.
            if (i > 0) stack.Children.Add(CastShadow(i * Edge));
            stack.Children.Add(face);
        }
        CardPane.Children.Add(stack);

        var lines = new StackPanel { Spacing = 12 };
        foreach (var rule in shown.Rules)
        {
            var line = new Grid { ColumnSpacing = 16 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            var name = new StackPanel { Spacing = 2 };
            name.Children.Add(new TextBlock { Text = rule.Label, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = Look("TafsInkText") });
            name.Children.Add(Words(rule.OutsideWords(w), 12, "TafsDimText"));
            var limits = Words(rule.Summary(w), 13, rule.Never ? "TafsNoText" : "TafsInkText");
            limits.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas");
            limits.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(limits, 1);
            line.Children.Add(name);
            line.Children.Add(limits);
            lines.Children.Add(line);
        }
        if (shown.Rules.Count > 0) CardPane.Children.Add(Panel(lines));

        // A revoked taf can't be halted. Remove for good is on Setup, as the iPad's.
        if (revoked) return;
        // Halting is a job of its own too, for whoever manages the taf: its row opens its screen (the iPad's HaltRow).
        if (opening.Halt is { } halt) CardPane.Children.Add(HaltRow(halt));
        // "Changes to how it runs" and Usage, as the iPad's, for whoever manages it.
        AddCardRows();
        // Revoke reaches nothing yet, so it's the sample's only: on a real taf it would look done when KIFF revoked nothing.
        if (opening?.CloudId is not null) return;
        var revoke = new Button { Content = w.Get("taf.revoke"), Style = Look("TafsOutlineNo"), HorizontalAlignment = HorizontalAlignment.Stretch };
        revoke.Click += async (_, _) => await RevokeAsync();
        CardPane.Children.Add(revoke);
    }

    /// <summary>
    /// The row that opens the taf's halt, with where it stands (the iPad's <c>HaltRow</c>, framed): the hand in red, "Halt",
    /// "Stop it until you resume" or its state in red, and the caret. The whole row opens it.
    /// </summary>
    /// <summary>Done, for the window: the taf is gone, so Home is read again and the Overview opens.</summary>
    public static event Action? Removed;

    /// <summary>
    /// "Remove Rae", outlined in red, as the iPad's: its confirmation names what goes ("Remove Rae for good?", what always
    /// goes, then tafs cloud's words for its apps), Remove asks Windows Hello, and what came of it is said in its own
    /// dialog. Off while it goes.
    /// </summary>
    Button RemoveButton(TafRemove remove)
    {
        var w = App.Words;
        var name = opening!.Head.Name;
        var button = new Button
        {
            Content = w.Get("setup.remove_for_good"), Style = Look("TafsOutlineNo"), HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "taf.remove");
        // Narrator names the taf it removes: "Remove Refunds".
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, w.Get("taf.remove_named", ("taf", name)));
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                var goes = await remove.Goes();
                var ask = new ContentDialog
                {
                    XamlRoot = XamlRoot, Title = w.Get("taf.remove_title", ("taf", name)),
                    Content = new TextBlock { Text = TafRemoval.ConfirmText(w, goes), TextWrapping = TextWrapping.Wrap },
                    PrimaryButtonText = w.Get("taf.remove.button"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
                };
                ApprovalGate.Notice(ask);
                if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
                var result = await remove.Run();
                if (result.Words is { } said)
                    await new ContentDialog { XamlRoot = XamlRoot, Content = said, CloseButtonText = w.Get("common.ok") }.ShowAsync();
                if (result.Done) Removed?.Invoke();
            }
            catch (System.Runtime.InteropServices.COMException) { }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    Button HaltRow(TafHalt halt)
    {
        var w = App.Words;
        var line = Words(Tafs.Core.Taf.Halt.RowLine(w, Tafs.Core.Taf.Halt.Running), 13, "TafsDimText");
        var title = new TextBlock { Text = w.Get("taf.halt.title"), FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Style = Look("TafsInkText") };
        var words = new StackPanel { Spacing = 2, Children = { title, line } };
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var hand = new FontIcon { Glyph = ((char)0xE7E8).ToString(), FontSize = 16 };
        hand.Foreground = ChatViews.Themed("TafsNo");
        var caret = new FontIcon { Glyph = ((char)0xE76C).ToString(), FontSize = 12 };
        Grid.SetColumn(words, 1);
        Grid.SetColumn(caret, 2);
        row.Children.Add(hand);
        row.Children.Add(words);
        row.Children.Add(caret);
        var button = new Button { Style = Look("TafsBox"), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16), Content = row };
        AutomationProperties.SetName(button, $"{title.Text}. {line.Text}");
        button.Click += (_, _) => halt.Open();
        _ = ShowHaltAsync();
        return button;

        async Task ShowHaltAsync()
        {
            var h = await halt.Read();
            line.Text = Tafs.Core.Taf.Halt.RowLine(w, h);
            if (h.State != Tafs.Core.Taf.HaltState.Running) line.Style = Look("TafsNoText");
            AutomationProperties.SetName(button, $"{title.Text}. {line.Text}");
        }
    }

    /// <summary>
    /// The shadow a Card casts up onto the Card behind it, as the iPad's (black at 16%, radius 4, offset 2 up): a soft
    /// 6-high band over its top edge, inside its rounded corners. A gradient, so it shows in the screenshots too.
    /// </summary>
    static Microsoft.UI.Xaml.Shapes.Rectangle CastShadow(double top) => new()
    {
        Height = 6, Margin = new Thickness(10, top - 6, 10, 0), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
        Fill = new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.5, 0), EndPoint = new Windows.Foundation.Point(0.5, 1),
            GradientStops =
            {
                new Microsoft.UI.Xaml.Media.GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0, 0, 0, 0), Offset = 0 },
                new Microsoft.UI.Xaml.Media.GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0x29, 0, 0, 0), Offset = 1 },
            },
        },
    };

    /// <summary>A payment card's proportions, 85.6 by 54 mm, at most 300 wide, as the iPad's.</summary>
    const double CardWidth = 300;

    /// <summary>
    /// A Card's face: its name and environment pill, its figure and the words under it, its facts, who issued it and until
    /// when, in white on the taf's colour (a Card scoped to a test environment in grey), with the iPad's ring at its corner.
    /// </summary>
    Border Face(TafCard card, int depth)
    {
        var w = App.Words;
        var white = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        TextBlock Text(string text, double size, double opacity = 1, bool mono = false, bool bold = false)
        {
            var t = new TextBlock { Text = text, FontSize = size, Opacity = opacity, Foreground = white, TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal };
            if (mono) t.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas");
            return t;
        }
        var inside = new Grid { Padding = new Thickness(16), RowSpacing = 6 };
        foreach (var height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            inside.RowDefinitions.Add(new RowDefinition { Height = height });

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Text(card.Title, 12, mono: true, bold: true));
        if (card.EnvironmentLabel is { } env)
        {
            var pill = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 2, 6, 2),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x38, 0xFF, 0xFF, 0xFF)),
                Child = Text(env, 10, mono: true, bold: true) };
            Grid.SetColumn(pill, 1);
            top.Children.Add(pill);
        }
        inside.Children.Add(top);

        var middle = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom };
        if (card.Hero(w) is { } hero)
        {
            var figure = Text(hero.Figure, 34, bold: true);
            figure.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
            middle.Children.Add(figure);
            var words = Text(hero.Words, 12, 0.9);
            words.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
            middle.Children.Add(words);
        }
        Grid.SetRow(middle, 1);
        inside.Children.Add(middle);

        var facts = new StackPanel { Spacing = 2 };
        // A long fact shrinks to fit, to 80% at most as the iPad's minimumScaleFactor(0.8), and past that ends in "…".
        foreach (var fact in card.Facts(w))
        {
            var line = Text(fact, 11, 0.92, mono: true);
            line.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var room = CardWidth - 32;
            if (line.DesiredSize.Width > room) line.FontSize = Math.Max(11 * 0.8, Math.Floor(11 * room / line.DesiredSize.Width * 10) / 10);
            facts.Children.Add(line);
        }
        if (card.Rules.Count == 0) facts.Children.Add(Text(w.Get("card.no_actions_short"), 12, mono: true));
        Grid.SetRow(facts, 2);
        inside.Children.Add(facts);

        var bottom = new Grid();
        bottom.ColumnDefinitions.Add(new ColumnDefinition());
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var issued = new StackPanel { Spacing = 1 };
        issued.Children.Add(Text(w.Get("card.issued_by"), 10, 0.75));
        issued.Children.Add(Text(opening?.IssuedBy ?? "", 12, bold: true));
        var ends = new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Right };
        // A Card with no end says so, never a far-off date.
        var endWord = revoked ? "card.revoked" : !card.Active ? "card.ended" : card.Expires is null ? "card.ends" : "card.until";
        ends.Children.Add(Text(w.Get(endWord), 10, 0.75));
        ends.Children.Add(Text(card.Expires?.ToString("d MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo(w.Language == "pt" ? "pt-PT" : w.Language))
            ?? w.Get("card.no_end_date"), 12, bold: true));
        foreach (var t in ends.Children.OfType<TextBlock>()) t.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(ends, 1);
        bottom.Children.Add(issued);
        bottom.Children.Add(ends);
        Grid.SetRow(bottom, 3);
        inside.Children.Add(bottom);

        var layers = new Grid();
        layers.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 130, Height = 130, Margin = new Thickness(0, -45, -35, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Stroke = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)) });
        layers.Children.Add(inside);
        // Each Card behind a shade darker, so Cards of the same colour read apart.
        var shade = depth <= 0 ? 0 : Math.Min(0.12 + 0.08 * (depth - 1), 0.32);
        if (shade > 0) layers.Children.Add(new Border { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb((byte)(shade * 255), 0, 0, 0)) });

        // A Card scoped to another environment in the theme's dim, as the iPad's Theme.dim.
        var fill = card.Live ? RecordLineView.Colour(opening!.Head.Colour) : new Microsoft.UI.Xaml.Media.SolidColorBrush(ThemeColour("TafsDim"));
        if (revoked || !card.Active) fill.Opacity = 0.4;
        return new Border { Width = CardWidth, Height = Math.Round(CardWidth * 54 / 85.6), CornerRadius = new CornerRadius(18), Background = fill, Child = layers };
    }

    /// <summary>
    /// Revoke asks first, as the iPad's ("Revoke Refunds' Card?"), with Cancel the default. It reaches nothing yet: the Card
    /// tab is only the sample's, and on this page the sample's taf stops (its chip says Stopped, its Cards fade). The real
    /// Revoke comes in its own change.
    /// </summary>
    async Task RevokeAsync()
    {
        var w = App.Words;
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = w.Get("taf.revoke_title", ("taf", opening?.Head.Name ?? "")),
            Content = w.Get("taf.revoke_body"),
            PrimaryButtonText = w.Get("taf.revoke_and_stop"),
            CloseButtonText = w.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ActualTheme,
        };
        if (await ask.ShowAsync() != ContentDialogResult.Primary) return;
        revoked = true;
        ShowState();
        ShowCards();
    }

    /// <summary>
    /// A theme colour in this page's theme. Theme.xaml's dictionaries are merged into the app's, so they're looked for
    /// there, not in the app's own (empty) theme dictionaries. A colour code looks up never follows the window's theme.
    /// </summary>
    Windows.UI.Color ThemeColour(string key)
    {
        var theme = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var merged in Application.Current.Resources.MergedDictionaries.Prepend(Application.Current.Resources))
            if (merged.ThemeDictionaries.TryGetValue(theme, out var found) && found is ResourceDictionary d
                && d.TryGetValue(key, out var brush) && brush is Microsoft.UI.Xaml.Media.SolidColorBrush b)
                return b.Color;
        return Microsoft.UI.Colors.Gray;
    }

    TextBlock Eyebrow(string key) => new() { Text = Sidebar.Eyebrow(App.Words, key), Style = Look("TafsEyebrow") };

    static TextBlock Words(string text, double size, string style) =>
        new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Style = Look(style) };

    static Border Panel(params UIElement[] children)
    {
        var inside = new StackPanel { Spacing = 8 };
        foreach (var c in children) inside.Children.Add(c);
        return new Border { Style = Look("TafsCard"), Child = inside };
    }

    /// <summary>Draws the filters, the chosen one inverted, and the days it leaves.</summary>
    void Show()
    {
        if (opening is null) return;
        var w = App.Words;
        if (opening.CloudId is not null)
        {
            ShowCloud();
            return;
        }
        var page = opening.Activity(filter);
        FilterChips.Children.Clear();
        foreach (var f in page.Filters) FilterChips.Children.Add(Chip(f, f.Id == page.Filter));

        Days.ItemsSource = page.Groups.Select(g => new TafDayView(g.Title, g.Lines.Select(TafLineView.Of).ToList())).ToList();
        Empty.Text = TafActivity.Empty(w, page.Filter);
        Empty.Visibility = page.Groups.Count == 0 && page.EndWords is null ? Visibility.Visible : Visibility.Collapsed;
        EndWords.Text = page.EndWords ?? "";
        EndWords.Visibility = page.EndWords is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// tafs cloud's Activity, as the iPad's: the first page's filters, every page's days, then what ends them: why a read
    /// failed with Try again, the next page's ring, a read going, tafs cloud's end words, or "Nothing here.".
    /// </summary>
    void ShowCloud()
    {
        var pages = CloudPages();
        FilterChips.Children.Clear();
        foreach (var f in pages.Filters) FilterChips.Children.Add(Chip(f, f.Id == filter));
        Days.ItemsSource = pages.Groups.Select(g => new TafDayView(g.Title, g.Lines.Select(TafLineView.Of).ToList())).ToList();
        Empty.Visibility = Visibility.Collapsed;
        var end = pages.End(App.Words);
        EndWords.Text = end.Words ?? "";
        EndWords.Visibility = end.Kind is ActivityEndKind.EndWords or ActivityEndKind.NothingHere ? Visibility.Visible : Visibility.Collapsed;
        var ring = end.Kind is ActivityEndKind.Loading or ActivityEndKind.More;
        ActivityLoading.IsActive = ring;
        ActivityLoading.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
        ActivityError.Text = end.Words ?? "";
        ActivityFailed.Visibility = end.Kind == ActivityEndKind.Error ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A filter chip, as the Record's: 13, in a capsule; the chosen one inverts.</summary>
    Button Chip(ActivityFilter f, bool chosen)
    {
        var chip = new Button { Content = f.Title, FontSize = 13, Style = Look(chosen ? "TafsChipChosen" : "TafsChip") };
        AutomationProperties.SetName(chip, f.Title);
        if (chosen) AutomationProperties.SetItemStatus(chip, "selected");
        // A cloud taf reads the filter's newest page when the filter changes, as the iPad's .task(id: filter).
        chip.Click += (_, _) =>
        {
            if (f.Id == filter) return;
            filter = f.Id;
            Show();
            _ = LoadActivityAsync();
        };
        return chip;
    }

    static Style Look(string key) => (Style)Application.Current.Resources[key];
}

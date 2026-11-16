using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Tafs.Core.Account;
using Tafs.Core.Approvals;
using Tafs.Core.Calendar;
using Tafs.Core.Connections;
using Tafs.Core.Home;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Record;
using Tafs.Core.Taf;
using Tafs.Core.Usage;
using Tafs.Core.Pairing;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace Tafs.App;

public sealed partial class MainWindow : Window
{
    /// <summary>A laptop's window: the size the PR screenshots are taken at.</summary>
    static readonly SizeInt32 LaptopSize = new(1280, 800);

    /// <summary>The held calls Needs you shows: KIFF's, read with each refresh of Home's list, or the iPad's samples.</summary>
    IReadOnlyList<HeldCall> held;

    /// <summary>"Did this happen?": KIFF's money calls it can't prove, read when Needs you opens, and whether the person answers held calls.</summary>
    IReadOnlyList<Unproven> unproven = [];
    bool mayAnswerHolds = true;

    /// <summary>
    /// Answering held calls: whether the person manages Cards (KIFF's <c>cards.manage</c>, as the iPad's <c>canManageCards</c>),
    /// KIFF's lasting refusals by hold, and the holds whose answer is on its way.
    /// </summary>
    bool managesCards = true;

    /// <summary>KIFF's capabilities for the person, from <c>GET /v1/me</c>: "Allow this from now on" waits for <c>holds.allow</c>.</summary>
    IReadOnlyList<string>? capabilitiesRead;
    readonly Dictionary<string, string> lastingRefusals = [];
    readonly HashSet<string> sendingHolds = [];

    /// <summary>
    /// Your step, as the iPad's <c>yourSteps</c>: Home's, less those answered here that it still lists; those being answered;
    /// and why the last answer didn't go through (the iPad's <c>lastError</c>).
    /// </summary>
    IReadOnlyList<YourStep> listedSteps = [];
    IReadOnlyList<YourStep> steps = [];
    readonly HashSet<string> answeredSteps = [];
    readonly HashSet<string> answeringSteps = [];
    string? needsYouError;

    /// <summary>A screenshot's samples: they stay, and KIFF isn't read for them.</summary>
    readonly bool sampleHeld;

    /// <summary>Needs you's row and its title, for its count; the NEEDS YOU section's rows, drawn again with each read.</summary>
    NavigationViewItem? needsYouRow;
    string needsYouTitle = "";
    readonly List<object> heldRows = [];

    /// <summary>The holds this session has read, so only one held since the last read is notified; none before the first read.</summary>
    HashSet<string>? notified;

    /// <summary>
    /// The hold a clicked notification named, kept until Needs you shows its card, as the iPad's <c>requestedHold</c>: a
    /// click that started tafs, or a hold not read yet, lands on it once it's listed.
    /// </summary>
    string? requestedHold;

    /// <summary>
    /// KIFF's held calls read again every minute while a session is in use: the iPad hears of a new hold by push, which
    /// Windows doesn't have yet, so this is how a notification comes while tafs is open.
    /// </summary>
    readonly DispatcherTimer heldClock = new() { Interval = TimeSpan.FromMinutes(1) };

    /// <summary>When the window was minimised, if it is: past ten minutes the minute's read stops (<see cref="HoldToast.Reads"/>).</summary>
    DateTimeOffset? minimisedSince;

    /// <summary>The Record's lines: KIFF's activity once Home is read as the session, else none, or the iPad's sample.</summary>
    IReadOnlyList<RecordEntry> record;
    /// <summary>How this window reads KIFF for the Record: as the session, or a stand-in file for a screenshot; none signed out.</summary>
    Func<string, Task<CloudRead>>? kiffRead;
    /// <summary>The KIFF account signed in to (<c>GET /v1/me</c>): its name, the person's email and its time zone, once read.</summary>
    Tafs.Core.Account.KiffAccount? kiffAccount;

    /// <summary>
    /// The KIFF account, read once a session starts, as the iPad's <c>model.kiffAccount</c>: the email under Account in the
    /// sidebar, and the account's rows in Account. A read that fails leaves Account's title alone; one for an earlier session is
    /// dropped.
    /// </summary>
    async Task ReadKiffAccountAsync(int reading)
    {
        if (kiffRead is not { } read) return;
        var answer = (await read(Tafs.Core.Coach.CoachAccess.MePath)).Answer;
        if (reading != sessions || answer is not { } me) return;
        kiffAccount = Tafs.Core.Account.KiffAccount.Read(me);
        ShowKiffAccount();
    }

    /// <summary>Under Account at the sidebar's foot: the person's email, else the account's name; nothing until it's read.</summary>
    void ShowKiffAccount()
    {
        AccountName.Text = kiffAccount?.SidebarLine ?? "";
        AccountName.Visibility = kiffAccount is null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(AccountButton, Sidebar.Foot(App.Words, kiffAccount?.Name).AccountLabel + (kiffAccount?.Email is { } email ? ", " + email : ""));
        if (Pages.Content is AccountPage page) page.ShowAccount(kiffAccount);
    }
    /// <summary>The Overview's lines: tafs cloud's once Home is read, else none, or the sample's.</summary>
    IReadOnlyList<OverviewRow> overviewRows;
    /// <summary>Home's answer from tafs cloud, for a taf's page; none until it's read.</summary>
    HomeAnswer? home;
    /// <summary>How this window reads tafs cloud (as the session, or the contracts for a screenshot); none until Home is read.</summary>
    Func<string, Task<CloudRead>>? cloudRead;
    /// <summary>The same read with planque's proof, and the proof for a taf's Setup: the session's, none for a screenshot.</summary>
    Func<string, string?, Task<CloudRead>>? cloudReadWithProof;
    Func<Task<string?>>? setupProof;
    /// <summary>This device's id in KIFF, as the iPad's <c>deviceID</c>: read from KIFF before the first proof, and kept once found.</summary>
    string? deviceId;
    readonly bool sample;
    readonly UsageReads usage;
    readonly ConnectAppsList? connectApps;

    /// <summary>The view the Calendar opens on: Week; Day or Month for `--page calendar` or `calendar-month`.</summary>
    CalendarMode calendarOpensOn = CalendarMode.Week;

    /// <param name="page">A screen to open first: "pair" for Add this computer, "sign-in", "needs-you", "record", "calendar" (Day), "calendar-week", "calendar-month", "usage", "connect-apps", "account", "coach", or "taf" / "taf-setup" / "taf-card" (the sample's Refunds on its Activity, Setup or Card, with --sample). Otherwise the Overview.</param>
    /// <param name="viewOnly">The session can't approve yet: show the View only notice above every screen.</param>
    /// <param name="sample">Show the iPad's sample held calls and Record: for screenshots.</param>
    /// <param name="homeFile">Home's list from a contract file (contracts/home.json) in place of tafs cloud: for screenshots.</param>
    public MainWindow(string? screenshot, string? theme, string? page, bool viewOnly, bool sample = false, string? homeFile = null)
    {
        InitializeComponent();
        // Back at the front: Your tafs' order settles again, as the iPad's when the app comes back.
        Activated += OnReturned;
        // --theme for CI's screenshots, else the one chosen in Account last time, else the system's.
        Root.RequestedTheme = Element(ThemeChoices.AtLaunch(theme, screenshot is not null, () => ThemeChoices.Read(ThemeFile)));
        this.sample = sample;
        var w = App.Words;
        Title = w.Get("app.name");
        AppWindow.Resize(LaptopSize);

        Brand.Text = w.Get("app.name");
        AutomationProperties.SetName(BrandItem, w.Get("home.overview.title"));
        held = sample ? NeedsYouScreen.Sample(w, DateTimeOffset.UtcNow) : [];
        sampleHeld = sample;
        record = sample ? RecordScreen.Sample(w, DateTimeOffset.UtcNow) : [];
        usage = sample ? new(UsageScreen.SampleBalance(w), UsageScreen.SampleCredits(w), UsageScreen.SampleCredits(w, previous: true)) : new(null, null, null);
        connectApps = sample ? ConnectAppsScreen.Sample() : null;
        overviewRows = sample ? OverviewScreen.SampleRows(w, DateTimeOffset.UtcNow) : [];
        var at = Nav.MenuItems.IndexOf(BrandItem) + 1;
        foreach (var item in Sidebar.Screens(w))
        {
            var row = new NavigationViewItem
            {
                Content = item.Title,
                Tag = item.Screen,
                Icon = new FontIcon { Glyph = item.Glyph, Style = (Style)Application.Current.Resources["TafsSidebarIcon"] },
                Style = (Style)Application.Current.Resources["TafsSidebarItem"],
            };
            if (item.Screen == Screen.NeedsYou)
            {
                (needsYouRow, needsYouTitle) = (row, item.Title);
                ShowCount(row, w, item.Title);
            }
            Nav.MenuItems.Insert(at++, row);
        }
        AddHeldCalls(w);
        YourTafs.Content = Sidebar.Eyebrow(w, "home.your_tafs");
        // The tafs list comes from tafs cloud in its own change; until then none, or the sample's.
        var yourTafs = sample ? Sidebar.SampleYourTafs(w, DateTimeOffset.UtcNow) : [];
        AddYourTafs(yourTafs);
        NoTafs.Content = Sidebar.NoTafs(w);
        NoTafs.Visibility = yourTafs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var foot = Sidebar.Foot(w);
        CoachText.Text = foot.Coach;
        AutomationProperties.SetName(CoachButton, w.Get("coach.coach_mode"));
        AccountTitle.Text = foot.Account;
        AutomationProperties.SetName(AccountButton, foot.AccountLabel);

        var notice = PairingScreen.ViewOnly(w);
        ViewOnly.Title = notice.Title;
        ViewOnly.Message = notice.Lead;
        AddThisDevice.Content = notice.Add;
        // A kept session says how this PC approves: the notice shows exactly when it's view.
        ViewOnly.IsOpen = viewOnly || (screenshot is null && App.Sessions.Kept() is { Mode: ApprovalMode.View });

        SignInPage.PairInsteadChosen += OpenPairing;
        NeedsYouPage.AddThisDeviceChosen += OpenPairing;
        NeedsYouPage.StepAnswerChosen += (step, done) => _ = AnswerStepAsync(step, done);
        NeedsYouPage.HeldAnswerChosen += (call, answer) => _ = AnswerHeldAsync(call, answer);
        NeedsYouPage.AllowChosen += call => _ = OpenAllowAsync(call);
        Toasts.HoldChosen += hold => DispatcherQueue.TryEnqueue(() => OpenHold(hold));
        heldClock.Tick += (_, _) =>
        {
            if (home is { } listed && kiffRead is not null && HoldToast.Reads(minimisedSince, DateTimeOffset.UtcNow)) _ = ReadHeldAsync(listed, sessions);
        };
        // Minimised for more than ten minutes, the minute's read stops; the window coming back reads once.
        AppWindow.Changed += (window, change) =>
        {
            var minimised = window.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized };
            var now = DateTimeOffset.UtcNow;
            if (minimised && minimisedSince is null) minimisedSince = now;
            else if (!minimised && minimisedSince is { } since)
            {
                minimisedSince = null;
                if (!HoldToast.Reads(since, now) && home is { } listed && kiffRead is not null) _ = ReadHeldAsync(listed, sessions);
            }
        };
        // F5 on Needs you: Home and KIFF's held calls read again, as the iPad's pull-to-refresh.
        NeedsYouPage.RefreshChosen += () =>
        {
            if (cloudRead is not { } read) return;
            _ = ReadHomeAsync(read);
            // Did this happen? read again too, as the iPad's refresh then loadUnproven.
            _ = ReadUnprovenAsync(sessions);
        };
        ConnectAppsPage.AskCoachChosen += () => OnCoach(this, new RoutedEventArgs());
        // Focus goes back to the sidebar's Coach when Coach mode has gone.
        CoachMode.Closed += () => CoachButton.Focus(FocusState.Programmatic);
        OverviewPage.TafChosen += key => OpenBeside(typeof(TafPage), OpenTaf(w, key));
        // A result card in Coach: its taf, or Plan & usage, with Coach closed, as the iPad's beside its sidebar.
        CoachMode.Tafs = () => home?.Tafs ?? [];
        CoachMode.TafChosen += key =>
        {
            CoachMode.Close();
            OpenBeside(typeof(TafPage), OpenTaf(w, key));
        };
        CoachMode.HomeWanted += () =>
        {
            if (cloudRead is { } read) _ = ReadHomeAsync(read);
        };
        CoachMode.PlanChosen += () =>
        {
            CoachMode.Close();
            Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.Usage);
        };
        OverviewPage.NeedsYouChosen += () => Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.NeedsYou);
        SignInPage.SignedIn += session =>
        {
            ViewOnly.IsOpen = session.Mode == ApprovalMode.View;
            Nav.SelectedItem = BrandItem;
            if (App.Sessions.Kept() is { } kept) UseSession(kept);
        };
        // Paired with a key in the TPM: this PC approves, with its own passkey or the account's.
        PairingPage.Paired += session =>
        {
            ViewOnly.IsOpen = session.Mode == ApprovalMode.View;
            if (App.Sessions.Kept() is { } kept) UseSession(kept);
        };
        this.screenshot = screenshot;

        AccountPage.ThemeChosen += chosen =>
        {
            Root.RequestedTheme = chosen;
            if (screenshot is null) Keep(chosen);
        };
        AccountPage.SignInChosen += OpenSignIn;
        // The choices only for a KIFF account, as the iPad's mode == .kiff: never over the sample.
        AccountPage.AlertsChosen += () => OpenBeside(typeof(AlertsPage), new AlertsOpening(sample ? null : App.Sessions.Kept()?.Tenant));
        AccountPage.DevicesChosen += () => OpenBeside(typeof(DevicesPage), new DevicesOpening(kiffRead));
        // Connect apps and Plan & usage from Account, as the iPad's People & apps and Plan rows: the sidebar's screens.
        AccountPage.ConnectAppsChosen += () => Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.ConnectApps);
        AccountPage.UsageChosen += () => Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.Usage);
        AccountPage.PeopleChosen += () => OpenBeside(typeof(PeoplePage), new PeopleOpening(kiffRead,
            agent => home?.Tafs.FirstOrDefault(t => t.Agent == agent)?.Name));
        AccountPage.AddThisDeviceChosen += OpenPairing;
        // As the iPad's sign-out (its forgetSession): everything read as the last session goes, and nothing reads as anyone.
        AccountPage.SignedOut += () =>
        {
            ViewOnly.IsOpen = false;
            requestedHold = null;
            ForgetSession();
            OpenSignIn();
        };
        // Leaving the sample: the app starts again without it, as the iPad's Clear the sample leaves it.
        AccountPage.ClearSampleChosen += () => Microsoft.Windows.AppLifecycle.AppInstance.Restart(
            string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Where(a => a != "--sample")));

        if (page == "pair") OpenPairing();
        else if (page == "account") OnAccount(this, new RoutedEventArgs());
        else if (page == "sign-in") OpenSignIn();
        else if (page is "calendar" or "calendar-week" or "calendar-month")
        {
            // The reference set's two Calendar screenshots: Day, and Week (what the Calendar opens on).
            calendarOpensOn = page switch { "calendar" => CalendarMode.Day, "calendar-month" => CalendarMode.Month, _ => CalendarMode.Week };
            Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.Calendar);
        }
        else if (page == "usage") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.Usage);
        else if (page is "coach" or "coach-chats")
        {
            // Over the Overview, which stays in view beside it; with coach-chats, on Previous conversations.
            Nav.SelectedItem = BrandItem;
            CoachMode.Open(animate: false);
            if (page == "coach-chats") CoachMode.ShowPreviousChats();
        }
        else if (page is "gallery" or "gallery-job") galleryAsked = page;
        else if (page == "connect-apps") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.ConnectApps);
        else if (page == "record") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.Record);
        else if (page == "needs-you") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.NeedsYou);
        else if (page is "taf" or "taf-setup" or "taf-card" or "taf-chat" && sample)
            OpenBeside(typeof(TafPage), SampleTaf(w, page switch
            {
                "taf-setup" => TafTab.Setup, "taf-card" => TafTab.Card, "taf-chat" => TafTab.Chat, _ => TafTab.Activity,
            }));
        else Nav.SelectedItem = BrandItem;
        if (screenshot is not null) Root.Loaded += async (_, _) => await SaveAndQuitAsync(screenshot);
        // From files, every read is done before the window first draws, so the screenshot has them.
        if (homeFile is not null)
        {
            // The Record from a stand-in for KIFF's activity beside the Home contract (kiff-activity.json), if there is one.
            var activity = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(homeFile))!, "kiff-activity.json");
            kiffRead = _ => Task.FromResult(File.Exists(activity) ? CloudRead.Of(Tafs.Core.Contracts.ContractFile.Answer(activity)) : CloudRead.Unreached(w));
            // And a line's receipt from kiff-receipt.json, as KIFF writes it.
            var receipt = Path.Combine(Path.GetDirectoryName(activity)!, "kiff-receipt.json");
            ReceiptSheet.Read = _ => Task.FromResult<(byte[]?, string?)>(File.Exists(receipt) ? (File.ReadAllBytes(receipt), null) : (null, CloudRead.Unreached(w).Why));
            _ = ReadHomeAsync(path => Task.FromResult(ContractRead(w, homeFile, path)));
            // An approval card's grants and draft from stand-ins beside it, if there are any: planque-grants.json, as planque
            // writes it, and kiff-draft.json. Given before Coach's read, which reads them as it draws.
            var beside = Path.GetDirectoryName(Path.GetFullPath(homeFile))!;
            CoachMode.Grants = () => Task.FromResult(File.Exists(Path.Combine(beside, "planque-grants.json"))
                ? Tafs.Core.Kiff.PlanqueGrants.Read(File.ReadAllBytes(Path.Combine(beside, "planque-grants.json"))) : null);
            CoachMode.Drafts = _ => Task.FromResult(File.Exists(Path.Combine(beside, "kiff-draft.json"))
                ? Tafs.Core.Approvals.KiffDrafts.Read(Tafs.Core.Contracts.ContractFile.Answer(Path.Combine(beside, "kiff-draft.json"))) : null);
            CoachMode.Read = path => Task.FromResult(ContractRead(w, homeFile, path));
            // The first taf's page, its Activity from taf-activity.json beside the Home contract, its Chat from taf-conversation.json.
            if (page is "taf" or "taf-setup" or "taf-chat" && home?.Tafs.FirstOrDefault() is { } first)
                OpenBeside(typeof(TafPage), OpenTaf(w, first.Id, page switch { "taf-setup" => TafTab.Setup, "taf-chat" => TafTab.Chat, _ => TafTab.Activity }));
            // Connect apps from connect-apps.json beside the Home contract, as tafs cloud's list.
            // With planque-grants.json beside it, the account's connections too, ended with nothing confirmed.
            if (page is "connect-apps" or "connect-connections")
            {
                var grantsFile = Path.Combine(beside, "planque-grants.json");
                var calls = File.Exists(grantsFile) ? new ConnectionsCalls(
                    () => Task.FromResult(PlanqueGrants.Read(File.ReadAllBytes(grantsFile))),
                    g => home?.Tafs.FirstOrDefault(t => t.Agent == g.Request.Agent)?.Name, Manages: true,
                    (ids, _) => Task.FromResult((ids.Count, (string?)w.Get("common.not_done_unconfirmed"), false))) : null;
                Pages.Navigate(typeof(ConnectAppsPage), new ConnectAppsOpening(null, (path, _) => Task.FromResult(ContractRead(w, homeFile, path)), null,
                    calls, OpenFirst: page == "connect-connections"));
            }
        }
        else if (screenshot is null && !sample && App.Sessions.Kept() is { } session)
        {
            // Coach's thread comes through the same read, as the session; a screenshot never reads it.
            Root.Loaded += (_, _) => UseSession(session);
        }
    }

    /// <summary>
    /// Home's list, once the window is up: the Overview's lines and Your tafs, then each taf's Last result from its runs.
    /// An answer that can't be read leaves what it would fill as it was ("No tafs yet.", "–"). A screenshot never reads
    /// tafs cloud.
    /// </summary>
    async Task ReadHomeAsync(Func<string, Task<CloudRead>> read)
    {
        var w = App.Words;
        var reading = sessions;
        // Answered after another session began: it's the last session's, so it's dropped, as the iPad's accountGeneration.
        if ((await read(HomeList.Path)).Answer is not { } answer || reading != sessions) return;
        cloudRead = read;
        ShowHome(HomeList.Read(w, answer, TimeZoneInfo.Local));
        // Home's steps; a list that can't be read leaves them as they were.
        if (YourSteps.Read(answer) is { } listedNow) listedSteps = listedNow;
        ShowSteps();
        ShowHaltAll();
        _ = ReadGalleryAsync(read, reading);
        // Who manages each taf and the account's capabilities, read with the list as the iPad's refreshAccess, then every
        // taf's halt, now that tafs cloud can be read: whatever order KIFF's reads land in.
        if (App.Sessions.Kept()?.AccessToken is { } bearer) _ = AllowCoachAsync(bearer);
        var listed = home!;
        _ = ReadRecordAsync(listed, reading);
        _ = ReadHeldAsync(listed, reading);
        _ = ReadBriefAsync(read, reading);
        var reports = await Task.WhenAll(listed.Tafs.Select(t => TafRuns.LoadReportAsync(w, t.Id, read)));
        if (reading != sessions) return;
        overviewRows = HomeList.Rows(w, listed, TimeZoneInfo.Local, listed.Tafs.Select((t, i) => (t.Id, Report: reports[i])).ToDictionary(r => r.Id, r => r.Report));
        if (Pages.Content is OverviewPage overview) overview.ShowRows(overviewRows, home?.Labels);
    }

    /// <summary>
    /// The Record from KIFF's activity, with each refresh of Home's list, as the iPad's <c>refresh</c>: the calls of the tafs
    /// on <paramref name="listed"/>, by each taf's agent in KIFF. A read that fails, or that KIFF answers in a way it can't
    /// read, leaves the Record as it was, as the iPad's does; one that lands after another session began is dropped.
    /// </summary>
    async Task ReadRecordAsync(HomeAnswer listed, int reading)
    {
        if (kiffRead is not { } read) return;
        var answer = (await read(KiffActivity.Path)).Answer;
        if (reading != sessions || answer is not { } json || KiffActivity.Read(json) is not { } calls) return;
        var tafs = listed.Tafs.Where(t => t.Agent is not null).GroupBy(t => t.Agent!).ToDictionary(g => g.Key, g => (g.First().Name, g.First().Colour));
        record = KiffActivity.Lines(App.Words, calls, agent => tafs.TryGetValue(agent, out var taf) ? taf : null);
        if (Pages.Content is RecordPage page) page.ShowEntries(record);
        // The taf used last is known now: Your tafs' order can say so.
        ShowYourTafs();
    }

    /// <summary>
    /// Needs you from KIFF's held calls, with each refresh of Home's list, as the iPad's <c>refresh</c> and <c>show</c>: a
    /// plain read as the session, each hold under its taf by its agent. A read that fails, or that KIFF answers in a way it
    /// can't read, leaves Needs you as it was; one that lands after another session began is dropped.
    /// </summary>
    async Task ReadHeldAsync(HomeAnswer listed, int reading)
    {
        if (kiffRead is not { } read || sampleHeld) return;
        // Who answers, and who manages Cards, as the iPad's mayAnswerHolds and canManageCards: KIFF's capabilities on /v1/me.
        var capabilities = Tafs.Core.Coach.CoachAccess.Capabilities((await read(Tafs.Core.Coach.CoachAccess.MePath)).Answer);
        if (reading != sessions) return;
        (mayAnswerHolds, managesCards, capabilitiesRead) = (DidThisHappen.MayAnswerHolds(capabilities), capabilities?.Contains("cards.manage") ?? true, capabilities);
        var answer = (await read(KiffHeld.Path)).Answer;
        if (reading != sessions || answer is not { } json || KiffHeld.Read(json) is not { } holds) return;
        var tafs = listed.Tafs.Where(t => t.Agent is not null).GroupBy(t => t.Agent!).ToDictionary(g => g.Key, g => (g.First().Name, g.First().Colour));
        var calls = KiffHeld.Calls(App.Words, holds, agent => tafs.TryGetValue(agent, out var taf) ? taf : null);
        ShowHeld(calls);
        // A taf with a call waiting comes first.
        ShowYourTafs();
        // A hold KIFF has held since the last read gets its notification, as the iPad's hold alert, only for someone who
        // answers held calls (canAnswerHolds) and turned alerts on for this account (Account › Alerts); one no longer held
        // loses its notification (removeAlerts(forHold:)).
        Toasts.Remove(HoldToast.Gone(notified, calls));
        var alerts = Alerts.Prefs;
        if (HoldToast.Notifies(capabilities) && App.Sessions.Kept()?.Tenant is { } tenant && alerts.IsOn(tenant))
            foreach (var fresh in HoldToast.New(notified, calls))
                if (HoldToast.Lines(App.Words, fresh, TimeZoneInfo.Local, Toasts.Locked()) is { } lines) Toasts.Show(lines, alerts.Sound);
        notified = calls.Where(c => c.KiffId is not null).Select(c => c.KiffId!).ToHashSet();
    }

    /// <summary>
    /// A hold's notification was clicked, as the iPad's <c>PushDelegate.open</c>: the window comes forward, and Needs you
    /// opens with that hold's card in view. A hold no longer held just opens Needs you.
    /// </summary>
    internal void OpenHold(string hold)
    {
        requestedHold = hold;
        Activate();
        // The hold's card is in Needs you, under the brief when there is one.
        needsYouUnderBrief = true;
        if (needsYouRow is { } row)
        {
            if (ReferenceEquals(Nav.SelectedItem, row) && Pages.Content is not NeedsYouPage) ShowNeedsYou();
            Nav.SelectedItem = row;
        }
        ShowRequestedHold();
        // Not listed yet: KIFF is read again, as the iPad's onHoldAlert. Before sign-in, the first read brings it.
        if (requestedHold is not null && home is { } listed && kiffRead is not null) _ = ReadHeldAsync(listed, sessions);
    }

    /// <summary>The requested hold's card brought into view, once Needs you lists it; then it's forgotten.</summary>
    void ShowRequestedHold()
    {
        if (requestedHold is { } hold && Pages.Content is NeedsYouPage page && page.ShowHold(hold)) requestedHold = null;
    }

    /// <summary>
    /// "Did this happen?" read from KIFF when Needs you opens, as the iPad's <c>loadUnproven</c>: only for someone who answers
    /// held calls (KIFF's <c>holds.answer</c>, from <c>GET /v1/me</c>), a plain read as the session. A read that fails keeps
    /// the list; one that lands after another session began is dropped.
    /// </summary>
    async Task ReadUnprovenAsync(int reading)
    {
        if (kiffRead is not { } read || sampleHeld) return;
        // Read into locals first: an answer from an earlier session never replaces this one's, as the iPad's accountGeneration.
        var capabilities = Tafs.Core.Coach.CoachAccess.Capabilities((await read(Tafs.Core.Coach.CoachAccess.MePath)).Answer);
        if (reading != sessions) return;
        (mayAnswerHolds, managesCards, capabilitiesRead) = (DidThisHappen.MayAnswerHolds(capabilities), capabilities?.Contains("cards.manage") ?? true, capabilities);
        ShowHeld(held);
        if (mayAnswerHolds && (await read(DidThisHappen.Path)).Answer is { } json && reading == sessions && DidThisHappen.Read(json) is { } calls)
            unproven = calls;
        if (reading == sessions && Pages.Content is NeedsYouPage page) page.ShowUnproven(unproven, mayAnswerHolds);
        if (reading == sessions) ShowSteps();
    }

    /// <summary>How this PC answers the held calls now: none signed out or for the sample.</summary>
    HeldAnswering HeldAnsweringNow()
    {
        if (sampleHeld || App.Sessions.Kept() is not { } session) return HeldAnswering.None;
        var hasKey = TpmKey.Exists(session.KeyName);
        return new(mayAnswerHolds, call => HeldAnswers.CanAnswer(call, session.Tenant, hasKey), HeldAnswers.Approves(session.Mode, session.PasskeyId),
            managesCards, lastingRefusals, sendingHolds, call => HoldAllow.Offers(call, capabilitiesRead, managesCards, TafOf(call)));
    }

    /// <summary>The held call's taf in tafs cloud, by its agent, as the iPad's <c>cloudTaf(for:)</c>: none for one no taf has.</summary>
    string? TafOf(HeldCall call) =>
        call.Agent is { } agent ? home?.Tafs.FirstOrDefault(t => t.Agent == agent)?.Id : null;

    /// <summary>
    /// "Allow this from now on" on a held call, as the iPad's <c>openAllow</c>, <c>loadAllow</c> and <c>confirmAllow</c>: tafs
    /// cloud's offer made on the tap, KIFF's draft read by this PC, and one Windows Hello, this PC's passkey asserting the
    /// draft's confirm itself (with the held call when the switch is on). A sign-out meanwhile closes the sheet; taken, Needs
    /// you and Home read again.
    /// </summary>
    async Task OpenAllowAsync(HeldCall call)
    {
        var w = App.Words;
        if (sampleHeld || App.Sessions.Kept() is not { } session || call.KiffId is null) return;
        var reading = sessions;
        var bearer = session.AccessToken;
        var taf = TafOf(call);
        var calls = new AllowCalls(
            async round =>
            {
                if (taf is null) return (null, w.Get("needs_you.answer_not_taken"), false);
                var (read, why) = await HoldAllow.ReadAsync(w, taf, call, round,
                    async (path, body) =>
                    {
                        try { return await CloudSendAsync(bearer, path, body); }
                        catch (TaskCanceledException) { return CloudRead.Unreached(w); }
                    },
                    async path =>
                    {
                        // KIFF's draft; its refusal in the app's plain words (a 401 is signed out), as the iPad's kiff.draft(id:) and PlainError.
                        try
                        {
                            var (status, answer) = await KiffHttp.GetAsync(new Uri(Tafs.Core.SignIn.DeviceSignIn.Api, path), bearer, w.Language);
                            return status is >= 200 and <= 299 ? Answered(w, status, answer)
                                : new CloudRead(null, Tafs.Core.Kiff.KiffErrors.Words(w, status), status);
                        }
                        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException) { return CloudRead.Unreached(w); }
                    },
                    () => reading == sessions);
                return (read, why, reading != sessions);
            },
            async (sheet, body) =>
            {
                var confirmed = await HoldAllow.ConfirmAsync(w, sheet.Draft!.Id, body,
                    (path, signed) => HoldAllow.SignConfirmAsync(session,
                        session.PasskeyId is { } id ? new PasskeyApprover(new WindowsHello(() => App.WindowHandle), KiffSigning.FromBase64Url(id), Features.WindowsPasskeyApprovals) : null,
                        path, signed, SigningTime.Next(DateTimeOffset.UtcNow)),
                    (path, signed, signature) => KiffHttp.PostAsync(new Uri(Tafs.Core.SignIn.DeviceSignIn.Api, path), signed, bearer, w.Language, signature: signature),
                    () => reading == sessions, SessionStore.Why);
                // The Card changed, and with the switch on the call went: Needs you and Home read again.
                if (confirmed.Done && cloudRead is { } again) _ = ReadHomeAsync(again);
                return confirmed;
            },
            HeldAnswers.Approves(session.Mode, session.PasskeyId), OpenPairing);
        await AllowDialog.ShowAsync(Root.XamlRoot, Root.ActualTheme, call, calls);
    }

    /// <summary>
    /// An answer to a held call, as the iPad's <c>answer(_:with:)</c>: approving once or extra room with a fresh Windows Hello
    /// passkey assertion over KIFF's <c>kiff.answer.v1</c>; refusing, with a next step or not, confirmed with Windows Hello and
    /// signed by the session's device key. Taken, the call leaves Needs you and Home is read again; not taken, why shows
    /// at the top and a lasting refusal stays with the call. A sign-out meanwhile ends it quietly.
    /// </summary>
    async Task AnswerHeldAsync(HeldCall call, HeldAnswer answer)
    {
        var w = App.Words;
        if (App.Sessions.Kept() is not { } session || sampleHeld || call.KiffId is not { } id || sendingHolds.Contains(id)) return;
        if (!held.Any(h => h.KiffId == id)) return;
        if (!HeldAnswers.CanAnswer(call, session.Tenant, TpmKey.Exists(session.KeyName)))
        {
            needsYouError = w.Get(TpmKey.Exists(session.KeyName) ? "needs_you.answer_in_kiff" : "needs_you.sign_in_again_to_answer");
            ShowSteps();
            return;
        }
        var reading = sessions;
        var bearer = session.AccessToken;
        sendingHolds.Add(id);
        needsYouError = null;
        ShowHeld(held);
        ShowSteps();
        var hello = new WindowsHello(() => App.WindowHandle);
        var credential = session.PasskeyId is { } passkeyId ? KiffSigning.FromBase64Url(passkeyId) : null;
        var answered = await HeldAnswers.AnswerAsync(w, call, answer, session.Tenant, session.Mode, session.PasskeyId,
            // The device key's answer asks for the person first, as the iPad's signer.sign(message, reason:).
            ConfirmStepAsync,
            message => credential is null
                ? Task.FromException<System.Text.Json.Nodes.JsonObject>(new PasskeyRefusedException("this PC has no passkey"))
                : HeldAnswers.PasskeyAsync(message, credential,
                    clientData => hello.GetAssertionAsync(PasskeyApprover.RpId, credential, clientData, CancellationToken.None)),
            async message =>
            {
                using var key = TpmKey.Open(session.KeyName);
                return Convert.ToBase64String(await key.SignDerAsync(message));
            },
            async (path, body) =>
            {
                try { return await KiffHttp.PostAsync(path, body, bearer: bearer); }
                catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException) { return (0, ""); }
            },
            () => SigningTime.Next(DateTimeOffset.UtcNow), () => reading == sessions);
        if (answered.Stopped || reading != sessions) return;
        sendingHolds.Remove(id);
        if (answered.Taken)
        {
            ShowHeld(held.Where(h => h.KiffId != id).ToList());
            if (cloudRead is { } read) await ReadHomeAsync(read);
            return;
        }
        needsYouError = answered.Why;
        if (answered.Lasting is { } code) lastingRefusals[id] = code;
        ShowHeld(held);
        ShowSteps();
    }

    /// <summary>Whether this PC can answer a step: the session's device key, and someone who answers the account's holds.</summary>
    bool CanAnswerSteps(KeptSession? session) =>
        YourSteps.CanAnswer(session is not null && !sampleHeld, mayAnswerHolds, session?.Tenant, session is not null && TpmKey.Exists(session.KeyName));

    /// <summary>The steps drawn again on Needs you when open, as the iPad's <c>showSteps</c>.</summary>
    void ShowSteps()
    {
        steps = YourSteps.Shown(listedSteps, answeredSteps);
        if (Pages.Content is NeedsYouPage page)
        {
            page.ShowSteps(steps, CanAnswerSteps(App.Sessions.Kept()), answeringSteps);
            page.ShowError(needsYouError);
        }
    }

    /// <summary>
    /// Done or Skip on a step, as the iPad's <c>answerYourStep</c>: the hand-off read from KIFF, confirmed with Windows Hello in
    /// tafs cloud's words, signed with the session's device key over <c>kiff.handoff.v1</c>, and posted. Answered or ended, it
    /// leaves the list; answered, Home is read again. Not answered, it stays with why. A sign-out meanwhile ends it quietly.
    /// </summary>
    async Task AnswerStepAsync(YourStep step, bool done)
    {
        var w = App.Words;
        var session = App.Sessions.Kept();
        if (session is null || sampleHeld || answeringSteps.Contains(step.Handoff) || answeredSteps.Contains(step.Handoff)) return;
        if (!CanAnswerSteps(session))
        {
            needsYouError = w.Get(TpmKey.Exists(session.KeyName) ? "needs_you.answer_in_kiff" : "needs_you.sign_in_again_to_answer");
            ShowSteps();
            return;
        }
        var reading = sessions;
        var bearer = session.AccessToken;
        answeringSteps.Add(step.Handoff);
        needsYouError = null;
        ShowSteps();
        var (outcome, why) = await YourSteps.AnswerAsync(w, step, done, session.Tenant,
            async path =>
            {
                try { return await KiffHttp.GetAsync(new Uri(Tafs.Core.SignIn.DeviceSignIn.Api, path), bearer, w.Language); }
                catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException) { return (0, ""); }
            },
            ConfirmStepAsync,
            async message =>
            {
                using var key = TpmKey.Open(session.KeyName);
                return Convert.ToBase64String(await key.SignDerAsync(message));
            },
            async (path, body) =>
            {
                try { return await KiffHttp.PostAsync(path, body, bearer: bearer); }
                catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException) { return (0, ""); }
            },
            () => SigningTime.Next(DateTimeOffset.UtcNow), () => reading == sessions);
        if (outcome == StepAnswered.Stopped || reading != sessions) return;
        answeringSteps.Remove(step.Handoff);
        needsYouError = why;
        if (outcome is StepAnswered.Answered or StepAnswered.Ended) answeredSteps.Add(step.Handoff);
        ShowSteps();
        // The lists read again, as the iPad's refresh once KIFF took the answer.
        if (outcome == StepAnswered.Answered && cloudRead is { } read) await ReadHomeAsync(read);
    }

    /// <summary>
    /// The person's confirmation before the device key signs, as the iPad's <c>deviceOwnerAuthentication</c>: Windows Hello
    /// (a face, a fingerprint or the PIN) over this window, with the prompt. Closed is cancelled; Hello not set up, or
    /// anything else, isn't confirmed.
    /// </summary>
    static async Task<StepConfirmed> ConfirmStepAsync(string prompt)
    {
        try
        {
            var result = await Windows.Security.Credentials.UI.UserConsentVerifierInterop.RequestVerificationForWindowAsync(App.WindowHandle, prompt);
            return result switch
            {
                Windows.Security.Credentials.UI.UserConsentVerificationResult.Verified => StepConfirmed.Yes,
                Windows.Security.Credentials.UI.UserConsentVerificationResult.Canceled => StepConfirmed.Cancelled,
                _ => StepConfirmed.Failed,
            };
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return StepConfirmed.Failed;
        }
    }

    /// <summary>The held calls drawn again: NEEDS YOU in the sidebar, Needs you's count, and Needs you or the Overview when open.</summary>
    void ShowHeld(IReadOnlyList<HeldCall> calls)
    {
        var w = App.Words;
        held = calls;
        NeedsYouPage.Answering = HeldAnsweringNow();
        AddHeldCalls(w);
        if (needsYouRow is { } row) ShowCount(row, w, NeedsYouTitle);
        if (Pages.Content is NeedsYouPage page) page.Show(held);
        ShowRequestedHold();
        if (Pages.Content is OverviewPage overview) overview.ShowHeld(held);
    }

    /// <summary>
    /// A read from tafs cloud as the session: its answer on a 2xx, else why not in the person's words, as the iPad's
    /// <c>TafsCloud.send</c>. Bodies are never logged.
    /// </summary>
    static async Task<CloudRead> CloudReadAsync(string bearer, string path, string? proof = null) =>
        await ReadAsync(new Uri(HomeList.Api, path), bearer, proof);

    /// <summary>A read as the session, from tafs cloud or KIFF, answered as <see cref="CloudRead"/>.</summary>
    static async Task<CloudRead> ReadAsync(Uri url, string bearer, string? proof = null)
    {
        var w = App.Words;
        int status;
        string body;
        try { (status, body) = await KiffHttp.GetAsync(url, bearer, w.Language, deviceSignature: proof); }
        catch (System.Net.Http.HttpRequestException) { return CloudRead.Unreached(w); }
        return Answered(w, status, body);
    }

    /// <summary>Which session Home and Coach read as: each sign-in moves it on, so a read that lands after it is dropped.</summary>
    /// <summary>
    /// What KIFF says this person may do: which tafs they manage, by agent (<c>GET /v1/me/access</c>), and the account's
    /// capabilities (<c>GET /v1/me</c>). None until read, which shows no halt.
    /// </summary>
    IReadOnlyDictionary<string, bool>? manage;
    IReadOnlyList<string>? capabilities;

    /// <summary>Every taf's halt, as last read, and its row in the sidebar (the iPad's <c>haltAllRow</c>).</summary>
    Halt accountHalt = Halt.Running;
    NavigationViewItem? haltAllRow;

    /// <summary>A halt sent to tafs cloud as the session: none signed out.</summary>
    Func<string, Task<CloudRead>>? haltSend;
    /// <summary>A POST to tafs cloud as the session, with its body: writing to a taf and Stop.</summary>
    Func<string, byte[], Task<CloudRead>>? cloudSend;
    /// <summary>A POST to tafs cloud as the session whose answer streams: a message to a taf.</summary>
    TafStream? cloudStream;

    int sessions;

    /// <summary>
    /// As the iPad's <c>AppModel.useSession</c>, at launch with a kept session and after each sign-in or pairing: Home and
    /// Coach read (and Coach answers) as this session from now on. What the last one read goes first: Home's list, the
    /// pages' reads (with Setup's proof, signed with this session's key), this device's id, Coach's thread and whether it may
    /// answer. Each sign-in and pairing keeps its session before it says so, so this reads the one just kept.
    /// </summary>
    void UseSession(KeptSession session)
    {
        var bearer = session.AccessToken;
        ForgetSession();
        cloudReadWithProof = (path, proof) => CloudReadAsync(bearer, path, proof);
        setupProof = () => SetupProofAsync(session);
        kiffRead = path => KiffReadAsync(bearer, path);
        _ = ReadKiffAccountAsync(sessions);
        ReceiptSheet.Read = path => KiffBytesAsync(bearer, path);
        // Given before Coach's read, which reads them as it draws its approvals.
        CoachMode.Grants = () => GrantsAsync(session);
        // Approve on Coach's approval cards: only a passkey session that knows its passkey signs; any other keeps the gate.
        // Given with the answers, once this person may use Coach (AllowCoachAsync).
        CoachMode.Approves = Tafs.Core.Coach.CoachStart.MayStart(session);
        CoachMode.Drafts = async id => (await KiffReadAsync(bearer, Tafs.Core.Approvals.KiffDrafts.Path(id))).Answer is { } d ? Tafs.Core.Approvals.KiffDrafts.Read(d) : null;
        CoachMode.Read = path => CloudReadAsync(bearer, path);
        haltSend = path => CloudSendAsync(bearer, path, "{}"u8.ToArray());
        cloudSend = (path, body) => CloudSendAsync(bearer, path, body);
        cloudStream = (path, body, cancel) => KiffHttp.PostStreamAsync(new Uri(HomeList.Api, path), body, bearer, App.Words.Language, cancel);
        // Start, as the iPad's: off for a session that can't approve.
        CoachMode.StartCoach = Tafs.Core.Coach.CoachStart.MayStart(session) ? () => StartCoachAsync(session) : null;
        // Signed in but can't approve: the iPad's gate line where Start would be.
        CoachMode.StartGate = !Tafs.Core.Coach.CoachStart.MayStart(session);
        // Reporting one of Coach's answers, as the iPad's reportAnswer: one answered after another session began is none.
        var reporting = sessions;
        CoachMode.Report = async (path, body) => await CloudSendAsync(bearer, path, body) is var r && reporting == sessions ? r : null;
        _ = ReadHomeAsync(path => CloudReadAsync(bearer, path));
        heldClock.Start();
        _ = AllowCoachAsync(bearer);
    }

    /// <summary>
    /// As the iPad's <c>forgetSession</c>, at sign-out and before each new session: what the last session read goes
    /// (Home's list, on the Overview and under Your tafs; the pages' reads and Setup's proof; this device's id; the Record's
    /// KIFF read and lines; Coach's thread, what was answered and whether it may answer), and a read it started that lands
    /// later is dropped. Until a
    /// session is used, nothing reads as anyone.
    /// </summary>
    void ForgetSession()
    {
        sessions++;
        // The last export's files too, as the iPad's removeOpenedFiles: a person's chats never stay on this PC after them.
        ChatsExport.Remove(ChatsExport.Folder);
        cloudRead = null;
        cloudReadWithProof = null;
        setupProof = null;
        deviceId = null;
        kiffRead = null;
        kiffAccount = null;
        ShowKiffAccount();
        ReceiptSheet.Read = null;
        record = [];
        if (Pages.Content is RecordPage page) page.ShowEntries(record);
        if (!sampleHeld) ShowHeld([]);
        unproven = [];
        mayAnswerHolds = true;
        ForgetOrder();
        // The gallery is the account's: its rows go.
        gallery = null;
        ShowGalleryRows();
        listedSteps = [];
        // The brief is the account's: Needs you is back in its place.
        (brief, briefLabel, needsYouUnderBrief, mayUseCoach) = (null, null, false, false);
        if (needsYouRow is { } briefRow) ShowCount(briefRow, App.Words, NeedsYouTitle);
        managesCards = true;
        capabilitiesRead = null;
        AllowDialog.Close();
        lastingRefusals.Clear();
        sendingHolds.Clear();
        answeredSteps.Clear();
        answeringSteps.Clear();
        needsYouError = null;
        ShowSteps();
        if (Pages.Content is NeedsYouPage needsYou) needsYou.ShowUnproven(unproven, mayAnswerHolds);
        notified = null;
        // The requested hold is kept: a session starting (a cold start from a click, or signing in after one) forgets the
        // last one's state first, and the click must still land once Needs you lists it. Only a sign-out drops it.
        Toasts.RemoveAll();
        heldClock.Stop();
        ShowHome(new HomeAnswer([], null));
        CoachMode.Read = null;
        CoachMode.Answer = null;
        (manage, capabilities, accountHalt, haltSend, cloudSend, cloudStream) = (null, null, Halt.Running, null, null, null);
        ShowHaltAll();
        CoachMode.StartCoach = null;
        CoachMode.StartGate = false;
        planqueReady = false;
        CoachMode.Report = null;
        CoachMode.Grants = null;
        CoachMode.Approve = null;
        CoachMode.Approves = false;
        CoachMode.Drafts = null;
        CoachMode.Forget();
    }

    /// <summary>This session set planque up, as the iPad's <c>planqueReady</c>: kept until sign-out, not across launches.</summary>
    bool planqueReady;

    /// <summary>
    /// Start, as the iPad's <c>startSetup</c> (<c>CoachStart</c>): planque set up first with the session's TPM key, as its
    /// <c>setUpPlanque</c> and <c>enrolPlanque</c>; then Setup's draft, signed by the device key or, in a passkey session, by
    /// this PC's passkey (its one Windows Hello), sent to KIFF; then tafs cloud claims its key.
    /// </summary>
    async Task<Tafs.Core.Coach.StartResult> StartCoachAsync(KeptSession session)
    {
        var w = App.Words;
        var bearer = session.AccessToken;
        var gen = sessions;
        try { return await StartCoachWithKeyAsync(session, bearer, gen); }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or FormatException)
        {
            // The TPM key gone or refusing, or a kept passkey id that can't be read, as the iPad's catch covers every error:
            // "Couldn't start." and why.
            return new(Tafs.Core.Coach.StartOutcome.Failed, w.Get("coach.couldnt_start", ("why", SessionStore.Why(e))));
        }
    }

    async Task<Tafs.Core.Coach.StartResult> StartCoachWithKeyAsync(KeptSession session, string bearer, int gen)
    {
        var w = App.Words;
        using var key = TpmKey.Open(session.KeyName);
        var hello = new WindowsHello(() => App.WindowHandle);
        var passkey = session.PasskeyId is { } id ? new PasskeyApprover(hello, KiffSigning.FromBase64Url(id), Features.WindowsPasskeyApprovals) : null;
        var kiff = Tafs.Core.SignIn.DeviceSignIn.Api;
        var calls = new Tafs.Core.Coach.StartCalls(
            ReadSetup: () => CloudReadAsync(bearer, Tafs.Core.Coach.CoachStart.SetupPath),
            Confirm: (credential, message) => new PasskeyApprover(hello, credential, Features.WindowsPasskeyApprovals).ConfirmAsync(message),
            SignDraft: (body, at) => Tafs.Core.Coach.CoachStart.SignDraftAsync(session, passkey, body, at),
            // The pair only by this PC's passkey: anything else fails closed and signs nothing.
            SignTogether: (connect, draft, at) => passkey is not null && session.Mode == ApprovalMode.Passkey
                ? Tafs.Core.Coach.RequestsTogether.SignAsync(passkey, session.Tenant, connect, draft, at)
                : throw new PasskeyRefusedException("only this PC's passkey signs the pair"),
            Enrol: () => PlanqueEnrolment.EnrolAsync(w, key.PublicKeyDer,
                async body =>
                {
                    var path = PlanqueEnrolment.Path(session.Tenant);
                    var signature = await PlanqueProof.SignAsync(session.Tenant, deviceId, LoadDeviceAsync, key,
                        () => SigningTime.Next(DateTimeOffset.UtcNow), "POST", path, body)
                        ?? throw new System.Net.Http.HttpRequestException("this device isn't listed in KIFF yet");
                    return await KiffHttp.PlanquePostAsync(new Uri(PlanqueProof.Api, path), body, signature, w.Language);
                },
                async () =>
                {
                    var signature = await PlanqueEnrolment.MintSignatureAsync(key, session.Tenant, SigningTime.Next(DateTimeOffset.UtcNow));
                    return await KiffHttp.PostAsync(new Uri(kiff, PlanqueEnrolment.KeysPath), PlanqueEnrolment.MintBody(), bearer, w.Language,
                        signature: ("KIFF-Device-Signature", signature));
                },
                // Signed out between planque's ask, KIFF's mint and the enrolment: nothing more is sent.
                () => gen == sessions),
            SendBatch: (body, signature) => KiffHttp.GatewayPostAsync(new Uri(Tafs.Core.Coach.CoachStart.Gateway, "/v1/connections/batch"), body,
                bearer, w.Language, signature),
            SubmitDraft: (body, signature) => KiffHttp.PostAsync(new Uri(kiff, Tafs.Core.Coach.CoachStart.DraftPath), body, bearer, w.Language,
                signature: signature),
            Claim: body => CloudSendAsync(bearer, Tafs.Core.Coach.CoachStart.SetupPath, body),
            SignedAt: () => SigningTime.Next(DateTimeOffset.UtcNow),
            Current: () => gen == sessions);
        var started = await Tafs.Core.Coach.CoachStart.StartAsync(w, session, planqueReady, calls);
        // Signed out meanwhile: it's no one's.
        if (gen != sessions) return new(Tafs.Core.Coach.StartOutcome.Cancelled);
        if (started.Started) planqueReady = true;
        return started;

        async Task<string?> LoadDeviceAsync()
        {
            var devices = await ReadAsync(new Uri(kiff, PlanqueProof.DevicesPath), bearer);
            var device = devices.Answer is { } answer ? PlanqueProof.ThisDevice(answer) : null;
            // A late answer never puts the last account's device on the next session's proofs.
            if (gen == sessions) deviceId = device;
            return device;
        }
    }

    /// <summary>
    /// Coach's cards are answered here only by someone who may use Coach (<c>CoachAccess</c>, as the iPad's
    /// <c>mayUseCoach</c>): KIFF's access to Setup, else the account's capabilities. Without it, Send stays off.
    /// </summary>
    async Task AllowCoachAsync(string bearer)
    {
        var reading = sessions;
        var access = (await KiffReadAsync(bearer, Tafs.Core.Coach.CoachAccess.AccessPath)).Answer;
        var me = Tafs.Core.Coach.CoachAccess.Capabilities((await KiffReadAsync(bearer, Tafs.Core.Coach.CoachAccess.MePath)).Answer);
        // Signed out meanwhile: it's no one's.
        if (reading != sessions || App.Sessions.Kept() is not { } session || session.AccessToken != bearer) return;
        var setup = Tafs.Core.Coach.CoachAccess.SetupUse(access);
        // Kept as it was when it can't be read, as the iPad's refreshAccess.
        manage = Tafs.Core.Coach.CoachAccess.Manage(access) ?? manage;
        // Which tafs are shared with this person to use: "Shared with you".
        ShowYourTafs();
        capabilities = me ?? capabilities;
        _ = ReadAccountHaltAsync(reading);
        mayUseCoach = Tafs.Core.Coach.CoachAccess.MayUseCoach(setup, setup is null ? capabilities : null);
        if (!mayUseCoach) return;
        CoachMode.Answer = (path, body) => CloudSendAsync(bearer, path, body);
        // Approve, as the iPad's Coach cards, only for someone who may use Coach, and only a session that signs with its passkey.
        if (Tafs.Core.Coach.CoachStart.MayStart(session))
            CoachMode.Approve = (card, conversation, digest, version) => ApproveCardAsync(session, card, conversation, digest, version);
    }

    /// <summary>Every taf's halt, for someone who manages Cards (the iPad's <c>haltAllRow</c>'s <c>refreshHalt(nil)</c>), then its row.</summary>
    async Task ReadAccountHaltAsync(int reading)
    {
        if (!Halt.ManagesCards(capabilities) || cloudRead is not { } read) { ShowHaltAll(); return; }
        var answer = (await read(Halt.Path(null))).Answer;
        if (reading != sessions) return;
        if (answer is { } json && Halt.Read(json) is { } h) accountHalt = h;
        ShowHaltAll();
    }

    /// <summary>
    /// Every taf's halt in the sidebar, under the screens, as the iPad's <c>showsAccountHalt</c>: to someone who manages Cards,
    /// while a taf runs, and whenever the account is halted or halting. The hand in red; under "Halt all", its state once it isn't running.
    /// </summary>
    void ShowHaltAll()
    {
        var w = App.Words;
        var shows = haltSend is not null && Halt.ShowsAccountHalt(Halt.ManagesCards(capabilities), home?.Tafs.Count > 0, accountHalt);
        if (!shows)
        {
            if (haltAllRow is not null) Nav.MenuItems.Remove(haltAllRow);
            haltAllRow = null;
            return;
        }
        var lines = new StackPanel { Spacing = 2 };
        lines.Children.Add(new TextBlock { Text = w.Get("taf.halt.title_all") });
        if (accountHalt.State != HaltState.Running)
            lines.Children.Add(new TextBlock { Text = Halt.Headline(w, accountHalt), FontSize = 12, Style = (Style)Application.Current.Resources["TafsNoText"] });
        if (haltAllRow is null)
        {
            haltAllRow = new NavigationViewItem
            {
                Tag = HaltAll,
                Icon = new FontIcon { Glyph = ((char)0xE7E8).ToString(), Style = (Style)Application.Current.Resources["TafsSidebarIcon"],
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TafsNo"] },
                Style = (Style)Application.Current.Resources["TafsSidebarItem"],
            };
            // Under the screens, as the iPad's first section: after the last of them.
            var at = Nav.MenuItems.OfType<NavigationViewItem>().Where(i => i.Tag is Screen).Select(i => Nav.MenuItems.IndexOf(i)).DefaultIfEmpty(Nav.MenuItems.IndexOf(BrandItem)).Max() + 1;
            Nav.MenuItems.Insert(at, haltAllRow);
        }
        haltAllRow.Content = lines;
        AutomationProperties.SetName(haltAllRow, string.Join(". ", lines.Children.OfType<TextBlock>().Select(t => t.Text)));
    }

    /// <summary>The sidebar's tag for every taf's halt.</summary>
    static readonly object HaltAll = new();

    /// <summary>The Halt screen for a taf by its tafs cloud id, or every taf with none.</summary>
    void OpenHalt(string? taf, string name)
    {
        if (cloudRead is not { } read || haltSend is not { } send) return;
        var reading = sessions;
        Pages.Navigate(typeof(HaltPage), new HaltOpening(taf, name, read, send, () =>
        {
            if (reading != sessions) return;
            // The list reads it halted (or running) at once, not at its next read, as the iPad's refresh() after a halt.
            _ = ReadHomeAsync(read);
            _ = ReadAccountHaltAsync(reading);
        }, ResumeFor(taf, name, reading)));
    }

    /// <summary>
    /// Resume for the Halt screen, only for a session that resumes (<see cref="HaltResume.MayResume"/>): this PC's passkey
    /// asserts KIFF's resume behind one Windows Hello, then tafs cloud releases its halt. None for any other session.
    /// </summary>
    Func<Task<(Halt? Halt, string? Why)>>? ResumeFor(string? taf, string name, int reading)
    {
        if (App.Sessions.Kept() is not { } session || !HaltResume.MayResume(session) || session.PasskeyId is not { } id) return null;
        var bearer = session.AccessToken;
        var agent = taf is null ? null : home?.Tafs.FirstOrDefault(t => t.Id == taf)?.Agent;
        return () =>
        {
            var hello = new WindowsHello(() => App.WindowHandle);
            var passkey = new PasskeyApprover(hello, KiffSigning.FromBase64Url(id), Features.WindowsPasskeyApprovals);
            return HaltResume.ResumeAsync(App.Words, taf, agent, taf is null ? null : name,
                path => HaltResume.SignAsync(session, passkey, path, SigningTime.Next(DateTimeOffset.UtcNow)),
                async (path, signature) => (await KiffHttp.PostAsync(new Uri(Tafs.Core.SignIn.DeviceSignIn.Api, path), HaltResume.Body(), bearer,
                    App.Words.Language, signature: signature)).Status,
                async path =>
                {
                    try
                    {
                        var (status, body) = await KiffHttp.DeleteAsync(new Uri(HomeList.Api, path), bearer, App.Words.Language);
                        return Answered(App.Words, status, body);
                    }
                    catch (System.Net.Http.HttpRequestException) { return CloudRead.Unreached(App.Words); }
                },
                () => reading == sessions && App.Sessions.Kept()?.AccessToken == bearer);
        };
    }

    /// <summary>
    /// The account's time zone set from this PC (<see cref="Tafs.Core.Account.AccountTimeZone.SetAsync"/>): Windows Hello as
    /// Your step asks it, then this session's TPM key signs KIFF's change. Taken, the account is read again, and Account and
    /// every time read after it follow. None without a session.
    /// </summary>
    Func<string, Task<(string? Zone, string? Why)>>? SetTimeZoneFor()
    {
        if (App.Sessions.Kept() is not { } session) return null;
        var reading = sessions;
        return async zone =>
        {
            var result = await Tafs.Core.Account.AccountTimeZone.SetAsync(App.Words, session.Tenant, zone, ConfirmStepAsync,
                async message =>
                {
                    using var key = TpmKey.Open(session.KeyName);
                    return await key.SignDerAsync(message);
                },
                async header => await KiffHttp.PutAsync(new Uri(Tafs.Core.SignIn.DeviceSignIn.Api, Tafs.Core.Account.AccountTimeZone.Path),
                    Tafs.Core.Account.AccountTimeZone.Body(zone), session.AccessToken, App.Words.Language, signature: ("KIFF-Device-Signature", header)),
                () => SigningTime.Next(DateTimeOffset.UtcNow), () => reading == sessions,
                // KIFF has it: tafs cloud moves the tafs' schedules to it.
                () => CloudWriteAsync(session.AccessToken, "POST", Tafs.Core.Account.AccountTimeZone.SchedulesPath, null));
            if (result.Zone is not null && reading == sessions)
            {
                await ReadKiffAccountAsync(reading);
                // The tafs' times are in it now: Home is read again.
                if (cloudRead is { } read) _ = ReadHomeAsync(read);
            }
            return result;
        };
    }

    /// <summary>
    /// Delete account (<see cref="Tafs.Core.Account.AccountDeletion.DeleteAsync"/>): Windows Hello as Your step asks it, then
    /// tafs cloud's erase, planque's (signed by this device), and KIFF's, last, signed by this session's TPM key. None without
    /// a session.
    /// </summary>
    Func<Task<(Tafs.Core.Account.Deleted Done, string? Why)>>? DeleteAccountFor()
    {
        if (App.Sessions.Kept() is not { } session) return null;
        var reading = sessions;
        var w = App.Words;
        return async () =>
        {
            using var key = TpmKey.Open(session.KeyName);
            return await Tafs.Core.Account.AccountDeletion.DeleteAsync(w, session.Tenant, ConfirmStepAsync,
                () => CloudWriteAsync(session.AccessToken, "POST", Tafs.Core.Account.AccountDeletion.CloudPath, Tafs.Core.Account.AccountDeletion.Body()),
                async () =>
                {
                    var path = Tafs.Core.Account.AccountDeletion.PlanquePath(session.Tenant);
                    // This device's id in KIFF, as Start's proof loads it: planque takes no signature without it.
                    async Task<string?> LoadDevice()
                    {
                        var devices = await KiffReadAsync(session.AccessToken, PlanqueProof.DevicesPath);
                        var device = devices.Answer is { } answer ? PlanqueProof.ThisDevice(answer) : null;
                        if (reading == sessions) deviceId = device;
                        return device;
                    }
                    var signature = await PlanqueProof.SignAsync(session.Tenant, deviceId, LoadDevice, key,
                        () => SigningTime.Next(DateTimeOffset.UtcNow), "DELETE", path, []);
                    return signature is null ? 0 : (await KiffHttp.PlanqueDeleteAsync(new Uri(PlanqueProof.Api, path), signature, w.Language)).Status;
                },
                async header => (await KiffHttp.PostAsync(new Uri(Tafs.Core.SignIn.DeviceSignIn.Api, Tafs.Core.Account.AccountDeletion.KiffPath),
                    Tafs.Core.Account.AccountDeletion.Body(), session.AccessToken, w.Language, signature: ("KIFF-Device-Signature", header))).Status,
                async message => await key.SignDerAsync(message),
                () => SigningTime.Next(DateTimeOffset.UtcNow), () => reading == sessions);
        };
    }

    /// <summary>A write to tafs cloud as the session (POST, PUT or DELETE), answered as a read is: Account's settings.</summary>
    static async Task<CloudRead> CloudWriteAsync(string bearer, string method, string path, byte[]? body)
    {
        var w = App.Words;
        var url = new Uri(HomeList.Api, path);
        try
        {
            var (status, answer) = method switch
            {
                "PUT" => await KiffHttp.PutAsync(url, body ?? "{}"u8.ToArray(), bearer, w.Language),
                "DELETE" => await KiffHttp.DeleteAsync(url, bearer, w.Language),
                _ => await KiffHttp.PostAsync(url, body ?? "{}"u8.ToArray(), bearer, w.Language),
            };
            return Answered(w, status, answer);
        }
        catch (System.Net.Http.HttpRequestException) { return CloudRead.Unreached(w); }
    }

    /// <summary>A read from KIFF as the session, answered as tafs cloud's are.</summary>
    static async Task<CloudRead> KiffReadAsync(string bearer, string path)
    {
        var w = App.Words;
        int status;
        string body;
        try { (status, body) = await KiffHttp.GetAsync(new Uri(Tafs.Core.SignIn.DeviceSignIn.Api, path), bearer, w.Language); }
        catch (System.Net.Http.HttpRequestException) { return CloudRead.Unreached(w); }
        return Answered(w, status, body);
    }

    /// <summary>
    /// A KIFF receipt as the session, as KIFF wrote it (the iPad's <c>KIFFClient.receipt</c>), or why not. Its words are
    /// written back as the UTF-8 they came as.
    /// </summary>
    static async Task<(byte[]? Raw, string? Why)> KiffBytesAsync(string bearer, string path)
    {
        var w = App.Words;
        int status;
        string body;
        try { (status, body) = await KiffHttp.GetAsync(new Uri(Tafs.Core.SignIn.DeviceSignIn.Api, path), bearer, w.Language); }
        catch (System.Net.Http.HttpRequestException) { return (null, CloudRead.Unreached(w).Why); }
        return status is < 200 or > 299 ? (null, CloudRead.Refused(w, status, body).Why) : (System.Text.Encoding.UTF8.GetBytes(body), null);
    }

    /// <summary>A card's answer sent to tafs cloud as the session, answered as a read is.</summary>
    static async Task<CloudRead> CloudSendAsync(string bearer, string path, byte[] sent)
    {
        var w = App.Words;
        int status;
        string body;
        try { (status, body) = await KiffHttp.PostAsync(new Uri(HomeList.Api, path), sent, bearer, w.Language); }
        catch (System.Net.Http.HttpRequestException) { return CloudRead.Unreached(w); }
        return Answered(w, status, body);
    }

    static CloudRead Answered(Tafs.Core.Words.WordBook w, int status, string body)
    {
        if (status is < 200 or > 299) return CloudRead.Refused(w, status, body);
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(body);
            return CloudRead.Of(json.RootElement.Clone());
        }
        catch (System.Text.Json.JsonException) { return CloudRead.Refused(w, status, ""); }
    }

    /// <summary>
    /// planque's signed read of the grants for a taf's Setup, as the iPad's <c>grantsReadProof</c>: signed with the TPM key
    /// the session is bound to, which asks no one, and this device's id in KIFF, read from KIFF's <c>GET /v1/me/devices</c>
    /// as the iPad's <c>deviceLoader</c>. None when the key isn't there or anything fails: Setup then reads without it.
    /// </summary>
    async Task<string?> SetupProofAsync(KeptSession session)
    {
        // Open only: a key gone with a sign-out throws, and Setup reads without the proof.
        using var key = TpmKey.Open(session.KeyName);
        var gen = sessions;
        return await PlanqueProof.GrantsReadAsync(session.Tenant, deviceId, async () =>
        {
            var devices = await ReadAsync(new Uri(Tafs.Core.SignIn.DeviceSignIn.Api, PlanqueProof.DevicesPath), session.AccessToken);
            var device = devices.Answer is { } answer ? PlanqueProof.ThisDevice(answer) : null;
            // A late answer never puts the last account's device on the next session's proofs.
            if (gen == sessions) deviceId = device;
            return device;
        }, key, () => SigningTime.Next(DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Approve on one of Coach's approval cards, as the iPad's <c>approve</c> (<see cref="Tafs.Core.Coach.CoachApproval.ApproveAsync"/>):
    /// tafs cloud's ask with planque's read proof, one Windows Hello (this PC's passkey asserting KIFF's confirm), the confirm,
    /// and the report signed by the TPM device key, which asks no one, with a fresh proof.
    /// </summary>
    async Task<Tafs.Core.Coach.ApproveResult> ApproveCardAsync(KeptSession session, Tafs.Core.Coach.CoachCard card, string? conversation,
        string? shownDigest, string? shownDraft)
    {
        var w = App.Words;
        var bearer = session.AccessToken;
        var gen = sessions;
        if (CoachMode.CoachId is not { } coach || session.PasskeyId is not { } passkeyId) return new(null);
        // As the iPad's approveAsked: planque set up by this session first, before anything is sent. It isn't kept across
        // launches here yet, so after one the person starts again from Coach's Start or Connected apps.
        if (!planqueReady) return new(w.Get("connect.start_connected_tools_first", ("device", w.Get("device.windows"))));
        var calls = new Tafs.Core.Coach.ApproveCalls(
            async () => { try { return await SetupProofAsync(session); } catch (System.Security.Cryptography.CryptographicException) { return null; } },
            async (path, body, proof) =>
            {
                try
                {
                    var (status, answer) = await KiffHttp.PostAsync(new Uri(HomeList.Api, path), body, bearer, w.Language,
                        signature: proof is null ? null : ("Planque-Device-Signature", proof));
                    return Answered(w, status, answer);
                }
                catch (System.Net.Http.HttpRequestException) { return CloudRead.Unreached(w); }
            },
            async (path, body) =>
            {
                var passkey = new PasskeyApprover(new WindowsHello(() => App.WindowHandle), KiffSigning.FromBase64Url(passkeyId), Features.WindowsPasskeyApprovals);
                return (PasskeyApprover.HeaderName, await passkey.ApproveAsync(session.Tenant, "POST", path, body, SigningTime.Next(DateTimeOffset.UtcNow)));
            },
            (path, body, signature) => KiffHttp.PostAsync(new Uri(Tafs.Core.SignIn.DeviceSignIn.Api, path), body, bearer, w.Language, signature: signature),
            async message =>
            {
                using var key = TpmKey.Open(session.KeyName);
                return await key.SignDerAsync(message);
            },
            async (path, body, signature, proof) =>
            {
                try
                {
                    var (status, answer) = await KiffHttp.PostAsync(new Uri(HomeList.Api, path), body, bearer, w.Language,
                        signature: ("KIFF-Device-Signature", signature), extra: proof is null ? null : ("Planque-Device-Signature", proof));
                    return Answered(w, status, answer);
                }
                catch (System.Net.Http.HttpRequestException) { return CloudRead.Unreached(w); }
            },
            () => SigningTime.Next(DateTimeOffset.UtcNow), () => gen == sessions, SessionStore.Why);
        try { return await Tafs.Core.Coach.CoachApproval.ApproveAsync(w, coach, card, session.Tenant, conversation, shownDigest, shownDraft, calls); }
        // A kept passkey id that can't be read, or anything else unforeseen: the card says why, and never stays waiting.
        catch (Exception e) when (e is FormatException or System.Security.Cryptography.CryptographicException)
        {
            return gen == sessions ? new(w.Get("coach.answer_not_reached", ("why", SessionStore.Why(e)))) : Tafs.Core.Coach.ApproveResult.Stopped;
        }
    }

    /// <summary>
    /// planque's grants for the approval cards, as the iPad's <c>refreshConnections</c>: read on planque as this device, with
    /// the same signature as Setup's proof (it signs this very read), never with KIFF's token. None without the proof, or when
    /// planque isn't reached, answers otherwise, or sends what can't be read.
    /// </summary>
    async Task<IReadOnlyList<Tafs.Core.Kiff.PlanqueGrant>?> GrantsAsync(KeptSession session)
    {
        if (await SetupProofAsync(session) is not { } proof) return null;
        try
        {
            var (status, body) = await KiffHttp.PlanqueGetAsync(new Uri(PlanqueProof.Api, PlanqueProof.GrantsPath(session.Tenant)), proof, App.Words.Language);
            return status is >= 200 and <= 299 ? Tafs.Core.Kiff.PlanqueGrants.Read(System.Text.Encoding.UTF8.GetBytes(body)) : null;
        }
        catch (System.Net.Http.HttpRequestException) { return null; }
    }

    /// <summary>
    /// For screenshots, tafs cloud's answers from the contracts beside <paramref name="homeFile"/>: Home's from it, a taf's
    /// runs from taf-runs.json, a run's report from taf-run-report.json, its Activity from taf-activity.json and its Setup from
    /// taf-setup.json; Coach's from setup-coach.json, coach-chats.json and coach-conversation.json; Connect apps' from
    /// connect-apps.json. With no such file, it reads as not reached.
    /// </summary>
    static CloudRead ContractRead(Tafs.Core.Words.WordBook w, string homeFile, string path)
    {
        // A taf's whole thread, when there's a stand-in for it; Coach's is read by its conversation's session.
        var tafThread = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(homeFile))!, "taf-conversation.json");
        var file = path == HomeList.Path ? homeFile
            : path.EndsWith("/conversation", StringComparison.Ordinal) && File.Exists(tafThread) ? tafThread
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(homeFile))!,
                path.StartsWith(ConnectAppsScreen.ListPath, StringComparison.Ordinal) ? "connect-apps.json"
                : path == Gallery.Path ? "gallery.json"
                : path.Contains("/activity?") ? "taf-activity.json" : path == "/v1/setup" ? "setup-coach.json"
                : path.StartsWith(Tafs.Core.Missions.Brief.Route, StringComparison.Ordinal) ? "brief.json"
                : path.EndsWith("/setup") ? "taf-setup.json" : path.EndsWith("/runs") ? "taf-runs.json"
                : path.EndsWith("/chats") ? "coach-chats.json" : path.Contains("/conversation") ? "coach-conversation.json" : "taf-run-report.json");
        return File.Exists(file) ? CloudRead.Of(Tafs.Core.Contracts.ContractFile.Answer(file)) : CloudRead.Unreached(w);
    }

    /// <summary>Home's answer on the Overview and under Your tafs, and for a taf's page.</summary>
    void ShowHome(HomeAnswer read)
    {
        var w = App.Words;
        home = read;
        overviewRows = HomeList.Rows(w, home, TimeZoneInfo.Local);
        // In their order and sections (MainWindow.Order.cs). As the iPad's sidebar: the line for no tafs only with none at
        // all, and with tafs but none relevant now, no Your tafs: they're all on the Overview.
        ShowYourTafs();
        NoTafs.Visibility = home.Tafs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Pages.Content is OverviewPage overview) overview.ShowRows(overviewRows, home?.Labels);
        // Coach's result cards name and open the tafs on this list.
        CoachMode.Redraw();
    }

    /// <summary>
    /// How many calls wait, at Needs you's row's end: a plain number in the soft colour, as the iPad's sidebar list
    /// draws a row's badge (not the tab bar's red pill). The row is named with it.
    /// </summary>
    void ShowCount(NavigationViewItem row, Tafs.Core.Words.WordBook w, string title)
    {
        var (count, name) = Sidebar.NeedsYouCount(w, held.Count);
        AutomationProperties.SetName(row, name);
        if (count is null)
        {
            row.Content = title;
            return;
        }
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        var number = new TextBlock
        {
            Text = count, FontWeight = Microsoft.UI.Text.FontWeights.Normal, Margin = new Thickness(8, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["TafsSidebarCount"],
        };
        Grid.SetColumn(number, 1);
        line.Children.Add(number);
        row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        row.Content = line;
    }

    /// <summary>A taf's row in the sidebar: it opens the taf's page.</summary>
    sealed record TafRow(string Key);

    /// <summary>
    /// Under YOUR TAFS, the tafs relevant now, as the iPad's sidebar: the avatar (34), the name in 15 semibold, and what
    /// it's doing in 13 soft, on one line each. A row opens its taf's page.
    /// </summary>
    /// <summary>
    /// Your tafs drawn again with each read of Home: a taf already listed keeps its row (so its selection stays), with its new
    /// lines; one no longer relevant loses its row; a new one is put in Home's order. Each read used to add its rows again,
    /// so a taf showed twice after a halt read Home once more.
    /// </summary>
    /// <param name="own">How many of <paramref name="rows"/> are the person's own: the rest go under "Shared with you".</param>
    void AddYourTafs(IReadOnlyList<OverviewRow> rows, int? own = null)
    {
        var mine = own ?? rows.Count;
        var listed = Nav.MenuItems.OfType<NavigationViewItem>().Where(i => i.Tag is TafRow).ToList();
        var keys = rows.Select(r => r.Key).ToHashSet();
        foreach (var gone in listed.Where(i => !keys.Contains(((TafRow)i.Tag).Key))) Nav.MenuItems.Remove(gone);
        var kept = listed.Where(i => keys.Contains(((TafRow)i.Tag).Key)).ToDictionary(i => ((TafRow)i.Tag).Key);
        var at = Nav.MenuItems.IndexOf(YourTafs) + 1;
        var index = 0;
        if (mine == 0) at = PlaceSharedHeader(at, rows.Count > 0);
        foreach (var taf in rows)
        {
            // Between the person's own tafs and those shared with them: "Shared with you".
            if (index++ == mine && mine > 0) at = PlaceSharedHeader(at, true);
            var content = new Grid { ColumnSpacing = 12 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition());
            content.Children.Add(new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = RecordLineView.Colour(taf.Colour),
                Child = new TextBlock { Text = TafScreen.Initial(taf.Taf), FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var lines = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            lines.Children.Add(new TextBlock { Text = taf.Taf, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            lines.Children.Add(new TextBlock { Text = taf.Now, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
                Style = (Style)Application.Current.Resources["TafsSoftText"] });
            Grid.SetColumn(lines, 1);
            content.Children.Add(lines);
            if (kept.TryGetValue(taf.Key, out var same))
            {
                // The same row, its lines new, moved to Home's order.
                same.Content = content;
                AutomationProperties.SetName(same, $"{taf.Taf}. {taf.Now}");
                var now = Nav.MenuItems.IndexOf(same);
                if (now != at) { Nav.MenuItems.RemoveAt(now); Nav.MenuItems.Insert(now < at ? --at : at, same); }
                at++;
                continue;
            }
            var row = new NavigationViewItem
            {
                Content = content, Tag = new TafRow(taf.Key), Style = (Style)Application.Current.Resources["TafsSidebarItem"],
                HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 56,
            };
            AutomationProperties.SetName(row, $"{taf.Taf}. {taf.Now}");
            Nav.MenuItems.Insert(at++, row);
        }
        if (mine >= rows.Count) PlaceSharedHeader(at, false);
    }

    /// <summary>
    /// NEEDS YOU above YOUR TAFS, as on the iPad: each held call on one row, an amber dot, "Taf: what it asks", and
    /// "reason · answer by 00:27" under it. A row opens Needs you. With none, the section isn't there.
    /// </summary>
    void AddHeldCalls(Tafs.Core.Words.WordBook w)
    {
        foreach (var old in heldRows) Nav.MenuItems.Remove(old);
        heldRows.Clear();
        if (!held.Any(c => c.Listed)) return;
        var at = Nav.MenuItems.IndexOf(YourTafs);
        var header = new NavigationViewItemHeader
        {
            Content = Sidebar.Eyebrow(w, "needs_you.title"),
            Style = (Style)Application.Current.Resources["TafsSidebarHeaderAmber"],
        };
        heldRows.Add(header);
        Nav.MenuItems.Insert(at++, header);
        foreach (var call in held.Where(c => c.Listed))
        {
            var (title, line) = NeedsYouScreen.SidebarRow(w, call, TimeZoneInfo.Local);
            var lines = new StackPanel { Spacing = 2 };
            lines.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            lines.Children.Add(new TextBlock { Text = line, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
                Style = (Style)Application.Current.Resources["TafsSoftText"] });
            // An 8-point amber dot in the icons' column, and the words where the rows' names start.
            var content = new Grid { ColumnSpacing = 10 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TafsAmber"],
            });
            Grid.SetColumn(lines, 1);
            content.Children.Add(lines);
            var row = new NavigationViewItem
            {
                Content = content,
                Tag = call,
                Style = (Style)Application.Current.Resources["TafsSidebarItem"],
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                MinHeight = 52,
            };
            AutomationProperties.SetName(row, $"{title}. {line}");
            heldRows.Add(row);
            Nav.MenuItems.Insert(at++, row);
        }
    }

    void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        ShowAccountChosen(false);
        // Another screen chosen: Needs you's row opens the brief again.
        if (item != needsYouRow) needsYouUnderBrief = false;
        if (item == BrandItem)
            Pages.Navigate(typeof(OverviewPage), new OverviewOpening(held, overviewRows, home?.Labels));
        else if (item.Tag is Screen.NeedsYou) ShowNeedsYouOrBrief();
        else if (item.Tag is HeldCall) ShowNeedsYou();
        else if (item.Tag == HaltAll) OpenHalt(null, "");
        else if (item.Tag is Screen.Record) Pages.Navigate(typeof(RecordPage), record);
        else if (item.Tag is Screen.Usage) Pages.Navigate(typeof(UsagePage), usage);
        else if (item.Tag is Screen.ConnectApps) Pages.Navigate(typeof(ConnectAppsPage), new ConnectAppsOpening(connectApps, cloudReadWithProof, setupProof, ConnectionsFor()));
        else if (item.Tag is Screen.Calendar) Pages.Navigate(typeof(CalendarPage), calendarOpensOn);
        else if (item.Tag is TafRow taf) Pages.Navigate(typeof(TafPage), OpenTaf(App.Words, taf.Key));
        else if (item.Tag is GalleryRow job) OpenGallery(job);
        else Pages.Navigate(typeof(ComingPage), item.Content);
    }

    /// <summary>Coach mode slides in from the right, over what's open, which stays in view.</summary>
    void OnCoach(object sender, RoutedEventArgs e) => CoachMode.Open();

    /// <summary>Account, at the very foot: it opens beside the sidebar, and its icon shows it's open.</summary>
    void OnAccount(object sender, RoutedEventArgs e)
    {
        OpenBeside(typeof(AccountPage), new AccountOpening(sample, Root.RequestedTheme,
            path => cloudRead is { } read ? read(path) : Task.FromResult(CloudRead.Unreached(App.Words)), kiffAccount,
            App.Sessions.Kept() is { } session ? (method, path, body) => CloudWriteAsync(session.AccessToken, method, path, body) : null,
            Halt.ManagesCards(capabilities), SetTimeZoneFor(), DeleteAccountFor()));
        ShowAccountChosen(true);
    }

    void ShowAccountChosen(bool chosen)
    {
        AccountIcon.Visibility = chosen ? Visibility.Collapsed : Visibility.Visible;
        AccountIconChosen.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The taf the reference set opens: the sample's Refunds, on its Activity (taf.png), its Setup (taf-setup.png) or its Card (taf-card.png).</summary>
    TafOpening SampleTaf(Tafs.Core.Words.WordBook w, TafTab opensOn) => SampleTaf(w, "refunds", opensOn);

    /// <summary>
    /// A taf's page from its line: from Home's answer, its head (its state in the app's words) and its Card bar; its
    /// Activity, Setup and Cards come with their reads. Otherwise the sample's.
    /// </summary>
    TafOpening OpenTaf(Tafs.Core.Words.WordBook w, string key, TafTab opensOn = TafTab.Chat) =>
        home?.Tafs.FirstOrDefault(t => t.Id == key) is { } taf
            ? new(TafScreen.Head(w, taf.Name, taf.Colour, taf.StateKey), taf.Cards.FirstOrDefault()?.Bar(w, taf.Cards.Count),
                filter => new ActivityPage([], [], filter, null, null), new SetupOpening(taf.Job, []), opensOn,
                // Its Cards, as Home read them from KIFF: the Card tab opens on every taf, as the iPad's. Who manages each taf
                // isn't read on this PC yet, so its issuer is the iPad's for anyone else: "The account".
                Cards: taf.Cards, IssuedBy: w.Get("card.issued_by_account"), Revoked: taf.Revoked,
                CloudId: taf.Id, Read: cloudRead, ReadWithProof: cloudReadWithProof, SetupProof: setupProof,
                Halt: HaltFor(taf), Send: cloudSend, Stream: cloudStream,
                // Stop is for whoever manages the taf (cards.manage), as KIFF says, once read.
                Manages: taf.Agent is { } agent && manage?.GetValueOrDefault(agent) == true)
            : SampleTaf(w, key, opensOn);

    /// <summary>
    /// A taf's halt row, only for whoever manages it as KIFF says (the iPad's <c>manages</c>): nothing until that's read.
    /// None for a revoked taf, as the iPad's (tafs cloud's "stopped", or any state it doesn't know), nor once it's revoked here.
    /// </summary>
    TafHalt? HaltFor(HomeTaf taf)
    {
        if (!Halt.HasRow(taf) || taf.Agent is not { } agent || manage?.GetValueOrDefault(agent) != true || cloudRead is not { } read || haltSend is null) return null;
        return new(async () => (await read(Halt.Path(taf.Id))).Answer is { } a && Halt.Read(a) is { } h ? h : Halt.Running,
            () => OpenHalt(taf.Id, taf.Name));
    }

    /// <summary>
    /// A sample taf's page, opened from its Overview line: its head (Waiting when a call of its waits), its Card bar, its
    /// Activity, its Setup's job and its Card. Refunds, the one the reference set shows, has its notes and its three Cards.
    /// </summary>
    TafOpening SampleTaf(Tafs.Core.Words.WordBook w, string key, TafTab opensOn)
    {
        var now = DateTimeOffset.UtcNow;
        var taf = SampleTafs.All(w).First(t => t.Key == key);
        var head = TafScreen.Head(w, taf.Name, taf.Colour, held.Any(h => h.Taf == taf.Name) ? "waiting" : "working");
        var activity = (Func<string, ActivityPage>)(filter => TafActivity.Sample(w, taf.Name, filter, now, TimeZoneInfo.Local));
        // Its Chat, as the iPad's sample: its greeting, with what it's doing as its Overview line says it.
        var chat = TafChat.Sample(w, taf.Name, OverviewScreen.SampleRows(w, now).FirstOrDefault(r => r.Key == key)?.Now ?? "", now);
        return key == "refunds"
            ? new(head, TafScreen.SampleCard(w), activity, new SetupOpening(TafSetup.SampleJob(w), TafSetup.SampleNotes(w, now)), opensOn,
                TafScreen.SampleCards(w), w.Get("common.you"), SampleChat: chat)
            // The others with their own Card and job, so every tab opens on each, as the iPad's.
            : new(head, taf.Card.Bar(w, 1), activity, new SetupOpening(w.Get($"sample.{key}.job"), []), opensOn, [taf.Card], w.Get("common.you"),
                SampleChat: chat);
    }

    /// <summary>Where Account's theme is kept, beside the session: one word, as the iPad keeps tafs.theme.</summary>
    static readonly string ThemeFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tafs", "theme");

    static ElementTheme Element(ThemeChoice theme) =>
        theme switch { ThemeChoice.Light => ElementTheme.Light, ThemeChoice.Dark => ElementTheme.Dark, _ => ElementTheme.Default };

    /// <summary>Keeps the theme for the next launch. One that can't be written still shows now; it's chosen again next time.</summary>
    static void Keep(ElementTheme theme)
    {
        try
        {
            ThemeChoices.Write(ThemeFile, theme switch { ElementTheme.Light => ThemeChoice.Light, ElementTheme.Dark => ThemeChoice.Dark, _ => ThemeChoice.System });
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>A screen that isn't a sidebar row: no row stays chosen while it's open.</summary>
    void OpenBeside(Type page, object? parameter = null)
    {
        Nav.SelectedItem = null;
        ShowAccountChosen(false);
        Pages.Navigate(page, parameter);
    }

    void OnAddThisDevice(object sender, RoutedEventArgs e) => OpenPairing();

    /// <summary>Add this computer with your phone. It isn't a sidebar item: it opens from the notice, and from sign-in.</summary>
    /// For a screenshot, it stays offline: no TPM key, no Windows Hello, no claim.
    void OpenPairing() => OpenBeside(typeof(PairingPage), screenshot is not null);

    readonly string? screenshot;

    /// <summary>Sign in with KIFF: the first screen of a PC with no session.</summary>
    void OpenSignIn() => OpenBeside(typeof(SignInPage));

    /// <summary>Saves the window as a PNG and quits: the screenshots posted with each PR that changes a screen.</summary>
    async Task SaveAndQuitAsync(string file)
    {
        await Task.Delay(500); // let the first layout settle
        // Launched with no input, the first focus counts as the keyboard's and draws its ring, which a person clicking
        // never sees: the same element, focused as by the pointer, keeps the shot as the screen looks.
        if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot) is Control focused) focused.Focus(FocusState.Pointer);
        await Task.Delay(100);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(Root);
        var pixels = await bitmap.GetPixelsAsync();

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();

        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        await File.WriteAllBytesAsync(Path.GetFullPath(file), bytes);
        Application.Current.Exit();
    }
}

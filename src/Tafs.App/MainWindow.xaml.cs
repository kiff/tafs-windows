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

    /// <summary>The held calls Needs you shows: from KIFF in their own change; until then none, or the iPad's samples.</summary>
    readonly IReadOnlyList<HeldCall> held;

    /// <summary>The Record's lines: KIFF's activity once Home is read as the session, else none, or the iPad's sample.</summary>
    IReadOnlyList<RecordEntry> record;
    /// <summary>How this window reads KIFF for the Record: as the session, or a stand-in file for a screenshot; none signed out.</summary>
    Func<string, Task<CloudRead>>? kiffRead;
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
        // --theme for CI's screenshots, else the one chosen in Account last time, else the system's.
        Root.RequestedTheme = Element(ThemeChoices.AtLaunch(theme, screenshot is not null, () => ThemeChoices.Read(ThemeFile)));
        this.sample = sample;
        var w = App.Words;
        Title = w.Get("app.name");
        AppWindow.Resize(LaptopSize);

        Brand.Text = w.Get("app.name");
        AutomationProperties.SetName(BrandItem, w.Get("home.overview.title"));
        held = sample ? NeedsYouScreen.Sample(w, DateTimeOffset.UtcNow) : [];
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
            if (item.Screen == Screen.NeedsYou) ShowCount(row, w, item.Title);
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
        AccountPage.AlertsChosen += () => OpenBeside(typeof(ComingPage), w.Get("settings.alerts"));
        AccountPage.AddThisDeviceChosen += OpenPairing;
        // As the iPad's sign-out (its forgetSession): everything read as the last session goes, and nothing reads as anyone.
        AccountPage.SignedOut += () =>
        {
            ViewOnly.IsOpen = false;
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
        else if (page == "coach")
        {
            // Over the Overview, which stays in view beside it.
            Nav.SelectedItem = BrandItem;
            CoachMode.Open(animate: false);
        }
        else if (page == "connect-apps") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.ConnectApps);
        else if (page == "record") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.Record);
        else if (page == "needs-you") Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag is Screen.NeedsYou);
        else if (page is "taf" or "taf-setup" or "taf-card" && sample)
            OpenBeside(typeof(TafPage), SampleTaf(w, page switch { "taf-setup" => TafTab.Setup, "taf-card" => TafTab.Card, _ => TafTab.Activity }));
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
            // The first taf's page, its Activity from taf-activity.json beside the Home contract.
            if (page is "taf" or "taf-setup" && home?.Tafs.FirstOrDefault() is { } first)
                OpenBeside(typeof(TafPage), OpenTaf(w, first.Id, page == "taf-setup" ? TafTab.Setup : TafTab.Activity));
            // Connect apps from connect-apps.json beside the Home contract, as tafs cloud's list.
            if (page == "connect-apps")
                Pages.Navigate(typeof(ConnectAppsPage), new ConnectAppsOpening(null, (path, _) => Task.FromResult(ContractRead(w, homeFile, path)), null));
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
        var listed = home!;
        _ = ReadRecordAsync(listed, reading);
        var reports = await Task.WhenAll(listed.Tafs.Select(t => TafRuns.LoadReportAsync(w, t.Id, read)));
        if (reading != sessions) return;
        overviewRows = HomeList.Rows(w, listed, TimeZoneInfo.Local, listed.Tafs.Select((t, i) => (t.Id, Report: reports[i])).ToDictionary(r => r.Id, r => r.Report));
        if (Pages.Content is OverviewPage overview) overview.ShowRows(overviewRows);
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
        ReceiptSheet.Read = path => KiffBytesAsync(bearer, path);
        // Given before Coach's read, which reads them as it draws its approvals.
        CoachMode.Grants = () => GrantsAsync(session);
        CoachMode.Drafts = async id => (await KiffReadAsync(bearer, Tafs.Core.Approvals.KiffDrafts.Path(id))).Answer is { } d ? Tafs.Core.Approvals.KiffDrafts.Read(d) : null;
        CoachMode.Read = path => CloudReadAsync(bearer, path);
        // Start, as the iPad's: off for a session that can't approve.
        CoachMode.StartCoach = Tafs.Core.Coach.CoachStart.MayStart(session) ? () => StartCoachAsync(session) : null;
        // Signed in but can't approve: the iPad's gate line where Start would be.
        CoachMode.StartGate = !Tafs.Core.Coach.CoachStart.MayStart(session);
        _ = ReadHomeAsync(path => CloudReadAsync(bearer, path));
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
        cloudRead = null;
        cloudReadWithProof = null;
        setupProof = null;
        deviceId = null;
        kiffRead = null;
        ReceiptSheet.Read = null;
        record = [];
        if (Pages.Content is RecordPage page) page.ShowEntries(record);
        ShowHome(new HomeAnswer([], null));
        CoachMode.Read = null;
        CoachMode.Answer = null;
        CoachMode.StartCoach = null;
        CoachMode.StartGate = false;
        planqueReady = false;
        CoachMode.Grants = null;
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
        var setup = Tafs.Core.Coach.CoachAccess.SetupUse((await KiffReadAsync(bearer, Tafs.Core.Coach.CoachAccess.AccessPath)).Answer);
        var capabilities = setup is null ? Tafs.Core.Coach.CoachAccess.Capabilities((await KiffReadAsync(bearer, Tafs.Core.Coach.CoachAccess.MePath)).Answer) : null;
        // Signed out meanwhile: it's no one's to answer.
        if (App.Sessions.Kept()?.AccessToken != bearer || !Tafs.Core.Coach.CoachAccess.MayUseCoach(setup, capabilities)) return;
        CoachMode.Answer = (path, body) => CloudSendAsync(bearer, path, body);
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
        var file = path == HomeList.Path ? homeFile
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(homeFile))!,
                path.StartsWith(ConnectAppsScreen.ListPath, StringComparison.Ordinal) ? "connect-apps.json"
                : path.Contains("/activity?") ? "taf-activity.json" : path == "/v1/setup" ? "setup-coach.json"
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
        var yours = HomeList.YourTafs(w, home, DateTimeOffset.UtcNow);
        AddYourTafs(yours);
        // As the iPad's sidebar: the line for no tafs only with none at all, and with tafs but none relevant now, no Your
        // tafs: they're all on the Overview.
        NoTafs.Visibility = home.Tafs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        YourTafs.Visibility = home.Tafs.Count > 0 && yours.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (Pages.Content is OverviewPage overview) overview.ShowRows(overviewRows);
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
        if (count is null) return;
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
    void AddYourTafs(IReadOnlyList<OverviewRow> rows)
    {
        var at = Nav.MenuItems.IndexOf(YourTafs) + 1;
        foreach (var taf in rows)
        {
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
            var row = new NavigationViewItem
            {
                Content = content, Tag = new TafRow(taf.Key), Style = (Style)Application.Current.Resources["TafsSidebarItem"],
                HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 56,
            };
            AutomationProperties.SetName(row, $"{taf.Taf}. {taf.Now}");
            Nav.MenuItems.Insert(at++, row);
        }
    }

    /// <summary>
    /// NEEDS YOU above YOUR TAFS, as on the iPad: each held call on one row, an amber dot, "Taf: what it asks", and
    /// "reason · answer by 00:27" under it. A row opens Needs you. With none, the section isn't there.
    /// </summary>
    void AddHeldCalls(Tafs.Core.Words.WordBook w)
    {
        if (held.Count == 0) return;
        var at = Nav.MenuItems.IndexOf(YourTafs);
        Nav.MenuItems.Insert(at++, new NavigationViewItemHeader
        {
            Content = Sidebar.Eyebrow(w, "needs_you.title"),
            Style = (Style)Application.Current.Resources["TafsSidebarHeaderAmber"],
        });
        foreach (var call in held)
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
            Nav.MenuItems.Insert(at++, row);
        }
    }

    void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        ShowAccountChosen(false);
        if (item == BrandItem)
            Pages.Navigate(typeof(OverviewPage), new OverviewOpening(held, overviewRows));
        else if (item.Tag is Screen.NeedsYou or HeldCall) Pages.Navigate(typeof(NeedsYouPage), held);
        else if (item.Tag is Screen.Record) Pages.Navigate(typeof(RecordPage), record);
        else if (item.Tag is Screen.Usage) Pages.Navigate(typeof(UsagePage), usage);
        else if (item.Tag is Screen.ConnectApps) Pages.Navigate(typeof(ConnectAppsPage), new ConnectAppsOpening(connectApps, cloudReadWithProof, setupProof));
        else if (item.Tag is Screen.Calendar) Pages.Navigate(typeof(CalendarPage), calendarOpensOn);
        else if (item.Tag is TafRow taf) Pages.Navigate(typeof(TafPage), OpenTaf(App.Words, taf.Key));
        else Pages.Navigate(typeof(ComingPage), item.Content);
    }

    /// <summary>Coach mode slides in from the right, over what's open, which stays in view.</summary>
    void OnCoach(object sender, RoutedEventArgs e) => CoachMode.Open();

    /// <summary>Account, at the very foot: it opens beside the sidebar, and its icon shows it's open.</summary>
    void OnAccount(object sender, RoutedEventArgs e)
    {
        OpenBeside(typeof(AccountPage), new AccountOpening(sample, Root.RequestedTheme));
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
    TafOpening OpenTaf(Tafs.Core.Words.WordBook w, string key, TafTab opensOn = TafTab.Activity) =>
        home?.Tafs.FirstOrDefault(t => t.Id == key) is { } taf
            ? new(TafScreen.Head(w, taf.Name, taf.Colour, taf.StateKey), taf.Cards.FirstOrDefault()?.Bar(w, taf.Cards.Count),
                filter => new ActivityPage([], [], filter, null, null), new SetupOpening(taf.Job, []), opensOn,
                CloudId: taf.Id, Read: cloudRead, ReadWithProof: cloudReadWithProof, SetupProof: setupProof)
            : SampleTaf(w, key, TafTab.Activity);

    /// <summary>
    /// A sample taf's page, opened from its Overview line: its head (Waiting when a call of its waits), its Card bar and its
    /// Activity. Refunds, the one the reference set shows, has its Setup and its Cards too.
    /// </summary>
    TafOpening SampleTaf(Tafs.Core.Words.WordBook w, string key, TafTab opensOn)
    {
        var now = DateTimeOffset.UtcNow;
        var taf = SampleTafs.All(w).First(t => t.Key == key);
        var head = TafScreen.Head(w, taf.Name, taf.Colour, held.Any(h => h.Taf == taf.Name) ? "waiting" : "working");
        var activity = (Func<string, ActivityPage>)(filter => TafActivity.Sample(w, taf.Name, filter, now, TimeZoneInfo.Local));
        return key == "refunds"
            ? new(head, TafScreen.SampleCard(w), activity, new SetupOpening(TafSetup.SampleJob(w), TafSetup.SampleNotes(w, now)), opensOn,
                TafScreen.SampleCards(w), w.Get("common.you"))
            : new(head, taf.Card.Bar(w, 1), activity);
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

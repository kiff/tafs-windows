using System.Globalization;
using Microsoft.UI.Xaml;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>
/// tafs for Windows. It draws what tafs cloud sends; this first shell has no network yet.
/// Options: --lang &lt;code&gt; to speak a shipped language, --theme light|dark, --page pair|pair-why|sign-in|needs-you|record|calendar|calendar-week|calendar-month|usage|usage-admin|connect-apps|account|coach|coach-wide|taf|taf-starts|taf-results|taf-versions|taf-usage|taf-setup|taf-card|taf-chat to open a screen,
/// --view-only to show the session as view-only, --sample for the iPad's sample held calls and Record, --home &lt;contract.json&gt; for Home's list from a file in place of tafs cloud, --new-version &lt;v1.0.2&gt; for the sidebar's "a new version is out" line without reading GitHub, and --screenshot &lt;file.png&gt; to save the window and quit.
/// </summary>
public partial class App : Application
{
    MainWindow? window;

    public App() => InitializeComponent();

    /// <summary>The words in the person's language, read from the pinned files beside the app.</summary>
    public static WordBook Words { get; private set; } = null!;

    /// <summary>The window Windows Hello opens over.</summary>
    public static IntPtr WindowHandle { get; private set; }

    /// <summary>This PC's kept session.</summary>
    internal static SessionStore Sessions { get; } = new();

    /// <summary>The languages the app ships (languages.json).</summary>
    internal static IReadOnlyCollection<string> Shipped { get; private set; } = [];

    /// <summary>Where Account's language choice is kept for this Windows user: beside the session, a bare code ("fr").</summary>
    static string LanguageFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "tafs", "language");

    /// <summary>The kept choice; none (Windows' languages) when there's none or it can't be read.</summary>
    internal static string? KeptLanguage()
    {
        try { return File.Exists(LanguageFile) ? AppLanguage.Kept(File.ReadAllText(LanguageFile), Shipped) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Account's picker, as Android's <c>AppLocale</c>: the choice is kept (none for this PC's own language) and tafs starts
    /// again at once in it, its session kept, since every screen's words are drawn as it opens. The one in use changes
    /// nothing. Not kept (a folder Windows won't write), it says why and nothing changes.
    /// </summary>
    internal static string? ChooseLanguage(string? language)
    {
        if (AppLanguage.Kept(language, Shipped) == KeptLanguage()) return null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LanguageFile)!);
            if (language is null) File.Delete(LanguageFile);
            else File.WriteAllText(LanguageFile, language);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Words.Get("error.ours"); }
        // The Windows App SDK starts the app again with the same arguments; if Windows won't, a new process does.
        var failed = Microsoft.Windows.AppLifecycle.AppInstance.Restart("");
        if (failed != Windows.ApplicationModel.Core.AppRestartFailureReason.RestartPending && Environment.ProcessPath is { } exe)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false });
            Current.Exit();
        }
        return null;
    }

    /// <summary>The direction the app's language reads in: right to left for Arabic and Hebrew, as the iPad mirrors.</summary>
    internal static FlowDirection Direction { get; private set; } = FlowDirection.LeftToRight;

    /// <summary>
    /// Right to left for Arabic and Hebrew. The window's root takes <see cref="Direction"/> (MainWindow); dialogs, flyouts and
    /// tooltips open in a popup layer that doesn't inherit it, so each takes it from an implicit style based on WinUI's own.
    /// </summary>
    static void FollowDirection(string language)
    {
        Direction = Locale.RightToLeft(language) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (Direction == FlowDirection.LeftToRight) return;
        foreach (var (type, key) in new[]
        {
            (typeof(Microsoft.UI.Xaml.Controls.ContentDialog), "DefaultContentDialogStyle"),
            (typeof(Microsoft.UI.Xaml.Controls.FlyoutPresenter), "DefaultFlyoutPresenterStyle"),
            (typeof(Microsoft.UI.Xaml.Controls.MenuFlyoutPresenter), "DefaultMenuFlyoutPresenterStyle"),
            (typeof(Microsoft.UI.Xaml.Controls.ToolTip), "DefaultToolTipStyle"),
        })
        {
            // Never an implicit style without WinUI's as its base: it would drop the control's template.
            if (!Current.Resources.TryGetValue(key, out var basis) || basis is not Style based) continue;
            var style = new Style(type) { BasedOn = based };
            style.Setters.Add(new Setter(FrameworkElement.FlowDirectionProperty, FlowDirection.RightToLeft));
            Current.Resources[type] = style;
        }
    }

    /// <summary>
    /// Spaced capitals (<c>TafsEyebrow</c>) are a Latin look: in Hangul, kana, Han and Arabic the monospaced face and the
    /// spacing are dropped, as the iPad's <c>Eyebrow.ideographic</c>, since spacing pulls Arabic's joined letters apart.
    /// </summary>
    static void FollowScript(string language)
    {
        // Monospaced text in a language whose letters join is drawn in the plain face (Monospace.Face).
        if (Tafs.Core.Words.Locale.JoinedScript(language)) Current.Resources["TafsMono"] = Microsoft.UI.Xaml.Media.FontFamily.XamlAutoFontFamily;
        if (language is not ("ja" or "ko" or "zh-Hant" or "ar")) return;
        if (!Current.Resources.TryGetValue("TafsEyebrow", out var found) || found is not Style eyebrow) return;
        // The style itself, changed before anything draws with it (a style is sealed once used): Theme.xaml's own templates
        // reach it inside their dictionary, where a replacement at the app's level wouldn't. Its size and theme-following
        // colour stay; only the face and the spacing change.
        if (eyebrow.IsSealed) return;
        foreach (var setter in eyebrow.Setters.OfType<Setter>().ToList())
            if (setter.Property == Microsoft.UI.Xaml.Controls.TextBlock.FontFamilyProperty
                || setter.Property == Microsoft.UI.Xaml.Controls.TextBlock.CharacterSpacingProperty)
                eyebrow.Setters.Remove(setter);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var options = Environment.GetCommandLineArgs();
        string? Option(string name)
        {
            var i = Array.IndexOf(options, name);
            return i >= 0 && i + 1 < options.Length ? options[i + 1] : null;
        }

        var strings = Path.Combine(AppContext.BaseDirectory, "Strings");
        var shipped = WordBook.Shipped(strings);
        // The person's own choice from Account's picker wins over Windows' languages; --lang, for screenshots, over both.
        Shipped = shipped;
        Words = WordBook.Load(strings, AppLanguage.Resolve(Option("--lang"), KeptLanguage(),
            Windows.System.UserProfile.GlobalizationPreferences.Languages.Append(CultureInfo.CurrentUICulture.Name), shipped));
        FollowDirection(Words.Language);
        FollowScript(Words.Language);

        // Notifications for held calls, registered before the window: a click on one can be what started tafs.
        var screenshot = Option("--screenshot");
        var startedAt = screenshot is null ? Toasts.Start() : null;

        window = new MainWindow(screenshot, Option("--theme"), Option("--page"), options.Contains("--view-only"),
            options.Contains("--sample"), Option("--home"), Option("--new-version"));
        WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        window.Activate();
        if (startedAt is not null) window.OpenHold(startedAt);
        if (screenshot is null) ListenForReturns();
    }

    /// <summary>
    /// planque's return from a vendor's sign-in (<c>tafs://planque/signed-in?…</c>), for whatever waits on it. Raised on the
    /// window's thread; what isn't a sign-in's return is ignored.
    /// </summary>
    public static event Action<Tafs.Core.Kiff.PlanqueSignedIn>? SignedInReturned;

    /// <summary>
    /// WhatsApp's sign-up page's return (<c>tafs://doors/whatsapp?…</c>), for the sign-up waiting on it, which takes it only
    /// with its own state. Raised on the window's thread.
    /// </summary>
    public static event Action<Tafs.Core.Taf.WhatsAppSignedUp>? WhatsAppReturned;

    /// <summary>
    /// The window as the one tafs: Windows hands it the <c>tafs:</c> scheme (registered for this user, as Windows App SDK
    /// does for an app that isn't packaged), and every later start's activation, brought to the front. A start that was
    /// itself a return passes it on.
    /// </summary>
    void ListenForReturns()
    {
        var me = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent();
        try
        {
            Microsoft.Windows.AppLifecycle.ActivationRegistrationManager.RegisterForProtocolActivation(
                Tafs.Core.Kiff.PlanqueSignIn.Scheme, "", "tafs", Environment.ProcessPath ?? "");
        }
        // A packaged build declares its scheme in its manifest instead; registering again is refused there.
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException) { }
        try
        {
            // KIFF's way back after its sign-in page (K5): dev.kiff.tafs:/oauth?…, to the attempt the welcome waits on.
            // Another app could register the same scheme; PKCE makes its code useless to it.
            Microsoft.Windows.AppLifecycle.ActivationRegistrationManager.RegisterForProtocolActivation(
                Tafs.Core.SignIn.AppSignIn.Scheme, "", "tafs", Environment.ProcessPath ?? "");
        }
        // A packaged build declares its scheme in its manifest instead; registering again is refused there.
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException) { }
        me.Activated += (_, activation) => window?.DispatcherQueue.TryEnqueue(() =>
        {
            window.Activate();
            Returned(activation);
        });
        Returned(me.GetActivatedEventArgs());
    }

    static void Returned(Microsoft.Windows.AppLifecycle.AppActivationArguments activation)
    {
        if (activation.Kind != Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Protocol
            || activation.Data is not Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocol) return;
        if (protocol.Uri.Scheme == Tafs.Core.SignIn.AppSignIn.Scheme)
        {
            // Only the waiting attempt's link is taken; any other is dropped.
            AppSignInFlow.Link(protocol.Uri.OriginalString);
            return;
        }
        if (Tafs.Core.Kiff.PlanqueSignIn.Read(protocol.Uri) is { } signedIn)
            SignedInReturned?.Invoke(signedIn);
        else if (Tafs.Core.Taf.WhatsAppDoor.Read(protocol.Uri) is { } signedUp)
            WhatsAppReturned?.Invoke(signedUp);
    }
}

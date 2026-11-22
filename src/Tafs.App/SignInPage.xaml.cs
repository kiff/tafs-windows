using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.Pairing;
using Tafs.Core.SignIn;

namespace Tafs.App;

/// <summary>
/// Signs this PC in with KIFF's device flow: the device key from the TPM, a passkey-only session (Windows
/// approves with a Hello passkey), the code checked in the browser. The session is kept in memory for now.
/// </summary>
public sealed partial class SignInPage : Page
{
    /// <summary>The person approved in the browser: the window opens tafs.</summary>
    public static event Action<SignedInSession>? SignedIn;

    /// <summary>A session the welcome's Continue made (in-app sign-in, <c>AppSignInFlow</c>), announced as this page's own, so the window moves on the same way.</summary>
    public static void Announce(SignedInSession session) => SignedIn?.Invoke(session);

    /// <summary>Add this PC with the phone instead.</summary>
    public static event Action? PairInsteadChosen;

    /// <summary>Navigated to with this, from the signed-out window's Continue: the code is asked for at once, in its half.</summary>
    public const string StartNow = "start";

    CancellationTokenSource cancel = new();
    SignInStep step = SignInStep.Welcome;
    /// <summary>In the signed-out window's half: no welcome (the half says it) and no "with your phone" (the other half is that).</summary>
    bool inHalf;
    SignInCode? code;

    public SignInPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => cancel.Cancel();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        WelcomeLead.Text = w.Get("sign_in.welcome.lead");
        InsideLead.Text = w.Get("sign_in.welcome.inside_lead");
        InsideRest.Text = w.Get("sign_in.welcome.inside_rest");
        OutsideLead.Text = w.Get("sign_in.welcome.outside_lead");
        OutsideRest.Text = w.Get("sign_in.welcome.outside_rest");
        RevokedLead.Text = w.Get("sign_in.welcome.revoked_lead");
        RevokedRest.Text = w.Get("sign_in.welcome.revoked_rest");

        var agreement = new Paragraph();
        foreach (var (text, link) in SignInScreen.Agreement(w))
        {
            if (link is null) { agreement.Inlines.Add(new Run { Text = text }); continue; }
            var hyperlink = new Hyperlink { NavigateUri = link };
            hyperlink.Inlines.Add(new Run { Text = text });
            agreement.Inlines.Add(hyperlink);
        }
        Agreement.Blocks.Add(agreement);
        inHalf = e.Parameter is StartNow;
        Show(SignInStep.Welcome);
        if (inHalf) _ = SignInAsync();
    }

    void Show(SignInStep next, string? why = null, string? status = null)
    {
        step = next;
        var lines = SignInScreen.Lines(App.Words, next, code, why);
        Heading.Text = lines.Title;
        Welcome.Visibility = Seen(next == SignInStep.Welcome && !inHalf);
        Lead.Text = lines.Lead ?? "";
        Lead.Visibility = Seen(lines.Lead is not null);
        CodeLabel.Text = lines.CodeLabel ?? "";
        Code.Text = lines.Code ?? "";
        CodeCard.Visibility = Seen(lines.Code is not null);
        Status.Text = status ?? lines.Status ?? "";
        StatusRow.Visibility = Seen(Status.Text.Length > 0);
        Busy.IsActive = next == SignInStep.Waiting;
        Busy.Visibility = Seen(next == SignInStep.Waiting);
        Primary.Content = lines.Button;
        // Signing in sends the device key and asks for a passkey-only session: only with Windows passkeys on.
        Primary.Visibility = Seen(lines.Button is not null && Features.WindowsPasskeyApprovals);
        OtherBrowser.Text = lines.OtherBrowser ?? "";
        Link.Text = lines.Link ?? "";
        Other.Visibility = Seen(lines.Link is not null);
        PairInstead.Content = lines.PairInstead;
        PairInstead.Visibility = Seen(lines.PairInstead is not null && !inHalf);
        Agreement.Visibility = Seen(next != SignInStep.Waiting);
    }

    static Visibility Seen(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    async void OnPrimary(object sender, RoutedEventArgs e)
    {
        if (step == SignInStep.Waiting && code is not null) await OpenAsync(code.VerificationUriComplete);
        else await SignInAsync();
    }

    void OnPairInstead(object sender, RoutedEventArgs e) => PairInsteadChosen?.Invoke();

    static Task<bool> OpenAsync(Uri link) => Windows.System.Launcher.LaunchUriAsync(link).AsTask();

    async Task SignInAsync()
    {
        cancel.Cancel();
        cancel = new CancellationTokenSource();
        var token = cancel.Token;
        Primary.IsEnabled = false;
        try
        {
            byte[] devicePub;
            try
            {
                using var key = TpmKey.OpenOrCreate();
                devicePub = key.PublicKeyDer;
            }
            catch (CryptographicException)
            {
                Show(step, status: App.Words.Get("sign_in.didnt_work")); // no TPM, or it refused
                return;
            }

            var body = DeviceSignIn.StartBody(devicePub, approvalPub: null, PairingScreen.DeviceName(App.Words));
            var (status, answer) = await KiffHttp.PostAsync(DeviceSignIn.StartPath, body, token);
            code = DeviceSignIn.ReadStart(status, answer, DateTimeOffset.UtcNow);
            if (code is null)
            {
                Show(SignInStep.CouldNotStart, SignInScreen.WhyStart(App.Words, DeviceSignIn.StartRefusal(status, answer)));
                return;
            }

            Show(SignInStep.Waiting);
            Primary.IsEnabled = true;
            await OpenAsync(code.VerificationUriComplete);

            var result = await DeviceSignIn.PollAsync(code,
                (b, c) => KiffHttp.PostAsync(DeviceSignIn.TokenPath, b, c),
                () => DateTimeOffset.UtcNow, Task.Delay, token);
            if (SignInScreen.After(result.Status) is { } next)
                Show(next, next == SignInStep.CouldNotFinish ? SignInScreen.Why(App.Words, result) : null);
            else
            {
                var s = result.Session!;
                // Kept across launches; a session it replaces is signed out.
                await App.Sessions.KeepAsync(KeptSession.Of(DeviceSignIn.Api, s.AccessToken, s.Tenant, s.Subject, null, s.Mode,
                    s.ExpiresIn, DateTimeOffset.UtcNow, TpmKey.DeviceKeyName));
                SignedIn?.Invoke(s);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException && code is not null)
        {
            Show(SignInStep.CouldNotFinish, SessionStore.Why(e)); // the session store refused
        }
        catch (HttpRequestException)
        {
            Show(code is null ? SignInStep.Welcome : SignInStep.Expired, status: App.Words.Get("error.offline"));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The page closed.
        }
        finally
        {
            Primary.IsEnabled = true;
        }
    }
}

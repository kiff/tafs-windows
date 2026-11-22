using System.Net.Http;
using System.Security.Cryptography;
using Tafs.Core.Kiff;
using Tafs.Core.Pairing;
using Tafs.Core.SignIn;

namespace Tafs.App;

/// <summary>
/// Continue on the welcome (K5, the iPad's <c>AppModel.signInInApp</c>, tafs-linux's <c>app_sign_in</c>): KIFF's sign-in in
/// the person's browser, the way back at <c>dev.kiff.tafs:/oauth</c> (Windows hands it to the running tafs, App's protocol
/// activation), then the token with the terms version the tick agreed to, kept as the device sign-in keeps a session.
/// One attempt at a time: a new Continue replaces the last one, whose link back is then never used. A code KIFF gave
/// before saying the terms changed is kept for 50 of its 60 seconds, so the new tick sends it again without opening
/// KIFF's page twice; past that, nothing is kept.
/// </summary>
static class AppSignInFlow
{
    /// <summary>How Continue ended: signed in (the session, kept), or why not.</summary>
    public sealed record Ended(SignedInSession? Session, AppSignIn.Outcome Outcome, string? Words);

    sealed record Waiting(AppSignIn.Attempt Attempt, string Terms, Action<Ended> Done);

    static Waiting? waiting;
    static AppSignIn.Kept? kept;

    /// <summary>
    /// Continue: opens KIFF's page in the browser for a fresh attempt, or, when KIFF just said the terms changed and its
    /// code is still good, sends that code again with the new version. <paramref name="done"/> hears once, on the
    /// window's thread.
    /// </summary>
    public static async Task BeginAsync(string language, string terms, Action<Ended> done)
    {
        if (AppSignIn.StillGood(kept, DateTimeOffset.UtcNow) && kept is { } k)
        {
            kept = null;
            done(await RedeemAsync(k.Attempt, k.Code, terms));
            return;
        }
        kept = null;
        var attempt = AppSignIn.Attempt.New();
        waiting = new Waiting(attempt, terms, done);
        if (!await Windows.System.Launcher.LaunchUriAsync(attempt.AuthorizeLink(language)))
        {
            waiting = null;
            done(new Ended(null, AppSignIn.Outcome.Failed, null));
        }
    }

    /// <summary>The welcome is gone, or the person started over: the attempt's link back is never used.</summary>
    public static void Forget()
    {
        waiting = null;
        kept = null;
    }

    /// <summary>
    /// A link Windows handed tafs. Only this attempt's is taken, once; any other is dropped, not even read as a refusal.
    /// </summary>
    public static async void Link(string link)
    {
        if (waiting is not { } w || w.Attempt.Answer(link) is not { } back) return;
        waiting = null;
        switch (back)
        {
            case AppSignIn.Back.Refused r:
                w.Done(new Ended(null, AppSignIn.BackRefused(r.Error), null));
                break;
            case AppSignIn.Back.Code c:
                var ended = await RedeemAsync(w.Attempt, c.Value, w.Terms);
                if (ended.Outcome == AppSignIn.Outcome.TermsChanged && ended.Session is null)
                    kept = new AppSignIn.Kept(w.Attempt, c.Value, DateTimeOffset.UtcNow);
                w.Done(ended);
                break;
        }
    }

    /// <summary>The code, with this attempt's verifier and the ticked terms, for a session kept on this PC.</summary>
    static async Task<Ended> RedeemAsync(AppSignIn.Attempt attempt, string code, string terms)
    {
        try
        {
            byte[] devicePub;
            using (var key = TpmKey.OpenOrCreate()) devicePub = key.PublicKeyDer;
            var device = DeviceSignIn.StartBody(devicePub, approvalPub: null, PairingScreen.DeviceName(App.Words));
            var (status, answer) = await KiffHttp.PostAsync(AppSignIn.TokenPath, attempt.TokenBody(code, terms, device), CancellationToken.None);
            var (session, outcome, words) = AppSignIn.ReadToken(status, answer);
            if (session is null) return new Ended(null, outcome, words);
            // Kept across launches; a session it replaces is signed out.
            await App.Sessions.KeepAsync(KeptSession.Of(DeviceSignIn.Api, session.AccessToken, session.Tenant, session.Subject, null,
                session.Mode, session.ExpiresIn, DateTimeOffset.UtcNow, TpmKey.DeviceKeyName));
            return new Ended(session, AppSignIn.Outcome.Failed, null);
        }
        catch (Exception e) when (e is HttpRequestException or CryptographicException or IOException or UnauthorizedAccessException)
        {
            return new Ended(null, AppSignIn.Outcome.Failed, null);
        }
    }

    /// <summary>The terms version in force, read as the welcome shows its tick; null when KIFF couldn't be read.</summary>
    public static async Task<string?> TermsAsync()
    {
        try
        {
            var (status, body) = await KiffHttp.PublicGetAsync(AppSignIn.TermsPath);
            return AppSignIn.ReadTerms(status, body);
        }
        catch (HttpRequestException) { return null; }
    }
}

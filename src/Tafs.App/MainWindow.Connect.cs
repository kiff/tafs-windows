using Tafs.Core.Approvals;
using Tafs.Core.Connections;
using Tafs.Core.Home;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// Connecting an app from Connect apps on this PC (P9): for whoever manages a taf, on a PC that approves with its own passkey.
/// tafs cloud starts the connection; planque's grant is read with KIFF's words for its profile; one Windows Hello opens KIFF's
/// approval window (kiff-cloud#1705); inside it this session's TPM key signs planque's approve and confirm as taps; the vendor's
/// sign-in opens in the browser and comes back through <c>tafs:</c>.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>How long the vendor's sign-in may take in the browser before the sheet stops waiting: planque's own 15 minutes.</summary>
    static readonly TimeSpan SignInWait = TimeSpan.FromMinutes(15);

    /// <summary>None for anyone who can't connect here: no session, no passkey of this PC's, or no taf this person manages.</summary>
    ConnectAppsConnecting? ConnectingFor()
    {
        if (App.Sessions.Kept() is not { } session || session.PasskeyId is not { } passkeyId || !HaltResume.MayResume(session) || home is not { } listed) return null;
        var tafs = listed.Tafs.Where(t => !t.Revoked && t.Agent is { } agent && manage?.GetValueOrDefault(agent) == true).Select(t => (t.Id, t.Name)).ToList();
        if (tafs.Count == 0) return null;
        var w = App.Words;
        var bearer = session.AccessToken;
        var reading = sessions;
        var kiff = Tafs.Core.SignIn.DeviceSignIn.Api;
        bool Current() => reading == sessions;

        async Task<((string Grant, bool NeedsKey)?, string?)> Start(ConnectApp app, string taf)
        {
            // As the iPad's: planque set up by this session first.
            if (!planqueReady) return (null, w.Get("connect.start_connected_tools_first", ("device", w.Get("device.windows"))));
            // A fresh signed grants read: planque takes each once. With it, tafs cloud decides the taf needs the app.
            string? proof;
            try { proof = await SetupProofAsync(session); }
            catch (System.Security.Cryptography.CryptographicException) { proof = null; }
            CloudRead answer;
            try
            {
                var (status, body) = await KiffHttp.PostAsync(new Uri(HomeList.Api, ConnectAppsScreen.StartPath(app.Entry)), ConnectAppsScreen.StartBody(taf),
                    bearer, w.Language, signature: proof is null ? null : ("Planque-Device-Signature", proof));
                answer = Answered(w, status, body);
            }
            catch (System.Net.Http.HttpRequestException) { answer = CloudRead.Unreached(w); }
            if (!Current()) return (null, null);
            if (answer.Answer is { } a && ConnectAppsScreen.Started(a) is { } started) return (started, null);
            // This taf doesn't use the app: tafs cloud's words, which send the person to Coach. Nothing started.
            if (answer.Code == "not_needed" && answer.Words is { } words) return (null, words);
            return (null, w.Get("connect.could_not_connected", ("vendor", app.DisplayName), ("why", answer.Why ?? w.Get("kiff.kiff_did_not_answer_expected"))));
        }

        async Task<(PlanqueGrant?, ProfileWords?, string?)> Read(string grantId)
        {
            var grants = await GrantsAsync(session);
            if (grants?.FirstOrDefault(g => g.Id == grantId) is not { } grant)
                return (null, null, grants is null ? w.Get("connect.approve_not_read", ("why", w.Get("error.offline"))) : w.Get("connect.no_such_connection"));
            if (grant.Profile is null) return (grant, null, null);
            var (words, why) = await ProfileWordsAsync(session, grant);
            return (grant, words, why);
        }

        var calls = ConnectCallsFor(session, passkeyId);

        return new ConnectAppsConnecting(tafs, Start, Read,
            (grantId, digest, words) => ConnectFlow.ApproveAsync(w, session.Tenant, grantId, digest, words, calls));
    }

    /// <summary>KIFF's words for exactly a grant's profile, read with the session and checked before they're shown; or why not.</summary>
    static async Task<(ProfileWords? Words, string? Why)> ProfileWordsAsync(KeptSession session, PlanqueGrant grant)
    {
        var w = App.Words;
        if (grant.Profile is not { } profile) return (null, null);
        try
        {
            var (status, body) = await KiffHttp.PostAsync(new Uri(Tafs.Core.SignIn.DeviceSignIn.Api, ProfileWords.Path), ProfileWords.Body(profile),
                session.AccessToken, w.Language);
            if (status is < 200 or > 299) return (null, w.Get("connect.kiff_words_connection_could_not"));
            return ProfileWords.Checked(w, System.Text.Encoding.UTF8.GetBytes(body), profile);
        }
        catch (System.Net.Http.HttpRequestException) { return (null, w.Get("connect.kiff_words_connection_could_not")); }
    }

    /// <summary>
    /// The calls connecting an app makes as this session (<see cref="ConnectCalls"/>), for Connect apps and Coach's approval card
    /// alike: planque's grants, KIFF's window read and opened by this PC's passkey, planque's taps signed by the TPM key in it, and
    /// the vendor's sign-in in the browser, back through <c>tafs:</c>.
    /// </summary>
    ConnectCalls ConnectCallsFor(KeptSession session, string passkeyId)
    {
        var w = App.Words;
        var bearer = session.AccessToken;
        var reading = sessions;
        var kiff = Tafs.Core.SignIn.DeviceSignIn.Api;
        bool Current() => reading == sessions;

        async Task<string?> Device()
        {
            if (deviceId is { } known) return known;
            var devices = await KiffReadAsync(bearer, PlanqueProof.DevicesPath);
            var device = devices.Answer is { } answer ? PlanqueProof.ThisDevice(answer) : null;
            if (Current()) deviceId = device;
            return device;
        }

        return new ConnectCalls(
            () => GrantsAsync(session),
            async () => (await KiffReadAsync(bearer, ApprovalWindow.Path)).Answer is { } a ? ApprovalWindow.Read(a) : null,
            async () =>
            {
                // One Windows Hello: this PC's own passkey approves opening the window, over exactly this request.
                var body = ApprovalWindow.OpenBody(DateTimeOffset.UtcNow);
                string header;
                try
                {
                    var passkey = new PasskeyApprover(new WindowsHello(() => App.WindowHandle), KiffSigning.FromBase64Url(passkeyId), Features.WindowsPasskeyApprovals);
                    header = await passkey.ApproveAsync(session.Tenant, "POST", ApprovalWindow.Path, body, SigningTime.Next(DateTimeOffset.UtcNow));
                }
                catch (Exception e) when (e is PasskeyRefusedException or InvalidDataException or FormatException or System.Runtime.InteropServices.COMException
                    or System.Security.Cryptography.CryptographicException or InvalidOperationException or UnauthorizedAccessException)
                {
                    return (null, e is PasskeyRefusedException ? e.Message : SessionStore.Why(e));
                }
                try
                {
                    var (status, answer) = await KiffHttp.PostAsync(new Uri(kiff, ApprovalWindow.Path), body, bearer, w.Language, signature: (PasskeyApprover.HeaderName, header));
                    var read = Answered(w, status, answer);
                    return read.Answer is { } a && ApprovalWindow.Read(a) is { } window ? (window, null)
                        : (null, read.Why ?? Tafs.Core.Kiff.KiffErrors.Words(w, status));
                }
                catch (System.Net.Http.HttpRequestException) { return (null, w.Get("error.offline")); }
            },
            async (window, path, body) =>
            {
                if (await Device() is not { } device) return (0, "{}");
                using var key = TpmKey.Open(session.KeyName);
                var header = await ApprovalWindow.PlanqueTapAsync(session.Tenant, device, window, key, SigningTime.Next(DateTimeOffset.UtcNow), "POST", path, body);
                return await KiffHttp.PlanquePostAsync(new Uri(PlanqueProof.Api, path), body, header, w.Language);
            },
            async (page, grant) =>
            {
                // The vendor's own page in the browser; planque sends it back to tafs with the one-use code for this grant.
                var back = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Returned(PlanqueSignedIn signedIn)
                {
                    if (signedIn.Tenant == session.Tenant && signedIn.Grant == grant) back.TrySetResult(signedIn.Code);
                }
                App.SignedInReturned += Returned;
                try
                {
                    if (!await Windows.System.Launcher.LaunchUriAsync(page)) return null;
                    var done = await Task.WhenAny(back.Task, Task.Delay(SignInWait));
                    return done == back.Task ? back.Task.Result : null;
                }
                finally { App.SignedInReturned -= Returned; }
            },
            Current, SessionStore.Why);

    }
}

using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Approvals;
using Tafs.Core.Home;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// Trying a version (the iPad's <c>TrialViews</c>): an open trial is its own row under its taf in the sidebar,
/// "Refunds · v2", and opens the taf's page on the trial's own chat; Try it on a version starts one.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>A trial's sidebar row's key: its taf's, marked, so the taf's own row keeps its key.</summary>
    const string TrialRowPrefix = "trial/";

    /// <summary>Your own tafs with each open trial on a row of its own under its taf, as the iPad's <c>tafLinks</c>.</summary>
    static List<OverviewRow> WithTrials(Tafs.Core.Words.WordBook w, HomeAnswer listed, IEnumerable<OverviewRow> own) =>
        own.SelectMany(r => listed.Tafs.FirstOrDefault(t => t.Id == r.Key)?.Trial is { } open
            ? [r, new OverviewRow(TrialRowPrefix + r.Key, open.ChatName(w, r.Taf), r.Colour, open.Line(w, TimeZoneInfo.Local), "", "")]
            : new[] { r }).ToList();

    /// <summary>A trial's row's badge, as the iPad's <c>taf.badge</c>: the trial's label, else "Trial"; none for any other row.</summary>
    string? TrialBadge(string key) =>
        key.StartsWith(TrialRowPrefix, StringComparison.Ordinal) && home?.Tafs.FirstOrDefault(t => t.Id == key[TrialRowPrefix.Length..])?.Trial is { } open
            ? open.Label?.Trim() is { Length: > 0 } label ? label : App.Words.Get("version.trial.badge")
            : null;

    /// <summary>A trial's row chosen: its taf's page on the trial's chat; none for a row that isn't one, or a trial since ended.</summary>
    TafOpening? TrialFromRow(string key) =>
        key.StartsWith(TrialRowPrefix, StringComparison.Ordinal) && home?.Tafs.FirstOrDefault(t => t.Id == key[TrialRowPrefix.Length..]) is { Trial: { } open } taf
            ? InTrial(taf, open) : null;

    /// <summary>
    /// The way into trials on a taf's Versions, for whoever manages it as KIFF says, on a PC that signs with its passkey (the
    /// trial's Card is confirmed by it, as Remove for good is). None for anyone else, and no Try it.
    /// </summary>
    TafTrying? TryingFor(HomeTaf taf)
    {
        if (taf.Agent is not { } agent || manage?.GetValueOrDefault(agent) != true || cloudRead is not { } read) return null;
        if (App.Sessions.Kept() is not { } session || !HaltResume.MayResume(session) || session.PasskeyId is null) return null;
        return new(
            () => TafTrials.LoadAsync(App.Words, taf.Id, read),
            n => TafTrials.CardAsync(App.Words, taf.Id, n, TrialCallsFor(session)),
            (n, shown) => TafTrials.StartAsync(App.Words, taf.Id, n, shown, TrialCallsFor(session)),
            open => OpenTrial(taf, open));
    }

    /// <summary>
    /// Install this version and Make it live, for whoever manages the taf as KIFF says, on a PC that signs with its passkey (a
    /// version's Card is confirmed by it). None for anyone else, and no Install.
    /// </summary>
    TafInstalling? InstallingFor(HomeTaf taf)
    {
        if (taf.Agent is not { } agent || manage?.GetValueOrDefault(agent) != true || cloudRead is null) return null;
        if (App.Sessions.Kept() is not { } session || !HaltResume.MayResume(session) || session.PasskeyId is null) return null;
        var w = App.Words;
        return new(
            (n, redraft, stopBlocking) => TafSwitch.BeginAsync(w, taf.Id, n, redraft, stopBlocking, TrialCallsFor(session)),
            draft => TafSwitch.CardAsync(w, draft, TrialCallsFor(session)),
            async (n, shown) =>
            {
                var result = await TafSwitch.InstallAsync(w, taf.Id, taf.Name, n, shown, new SwitchCalls(TrialCallsFor(session), ConfirmStepAsync));
                // A version that brought a Card changed the taf's limits: Home read again, so its page and list show it.
                if (result.Live && cloudRead is { } read) _ = ReadHomeAsync(read);
                return result;
            },
            () => TafSwitch.CancelAsync(w, taf.Id, TrialCallsFor(session)));
    }

    /// <summary>A trial just started or resumed: Home read again so its row is there, and its chat opened.</summary>
    void OpenTrial(HomeTaf taf, OpenTrial open)
    {
        Pages.Navigate(typeof(TafPage), InTrial(taf, open));
        if (cloudRead is { } read) _ = ReadHomeAsync(read);
    }

    /// <summary>
    /// The taf's page on its trial: the trial's chat, named "Refunds · v2", sent to and read from the trial; Go back and
    /// Install, then back to the taf with Home read again.
    /// </summary>
    TafOpening InTrial(HomeTaf taf, OpenTrial open)
    {
        var w = App.Words;
        var session = App.Sessions.Kept();
        Task<string?> Run(Func<TrialCalls, Task<string?>> act) =>
            session is null ? Task.FromResult<string?>(w.Get("error.signed_out")) : act(TrialCallsFor(session));
        Task<TrialInstall> Install(string? shown) =>
            session is null ? Task.FromResult(new TrialInstall(false, Why: w.Get("error.signed_out")))
                : TafTrials.InstallAsync(w, taf.Id, open, shown, TrialCallsFor(session));
        Task<(AgentDraft? Draft, string? Why)> Card() =>
            session is null ? Task.FromResult<(AgentDraft?, string?)>((null, w.Get("error.signed_out"))) : TafTrials.TrialCardAsync(w, open, TrialCallsFor(session));
        return OpenTaf(w, taf.Id, TafTab.Chat) with
        {
            Head = TafScreen.Head(w, open.ChatName(w, taf.Name), taf.Colour, taf.StateKey),
            Card = null, Stream = null, Manages = false, Halt = null, Remove = null, Trying = null, Installing = null, Starts = null, Health = null,
            InTrial = new TafInTrial(open, taf.Name,
                () => Run(calls => TafTrials.GoBackAsync(w, taf.Id, open, calls)),
                Card, Install,
                () =>
                {
                    Pages.Navigate(typeof(TafPage), OpenTaf(App.Words, taf.Id));
                    if (cloudRead is { } read) _ = ReadHomeAsync(read);
                }),
        };
    }

    /// <summary>
    /// A trial's calls as this session: tafs cloud's; KIFF's draft read; its confirm asserted by this PC's passkey with one
    /// Windows Hello; ending its Card signed by this session's TPM key (taking authority away, so no Windows Hello).
    /// </summary>
    TrialCalls TrialCallsFor(KeptSession session)
    {
        var w = App.Words;
        var bearer = session.AccessToken;
        var reading = sessions;
        var kiff = Tafs.Core.SignIn.DeviceSignIn.Api;
        return new(
            (method, path, body) => method == HttpMethod.Get ? cloudRead!(path) : CloudChangeAsync(bearer, method, path, body),
            async path =>
            {
                try
                {
                    var (status, answer) = await KiffHttp.GetAsync(new Uri(kiff, path), bearer, w.Language);
                    return status is >= 200 and <= 299 ? Answered(w, status, answer) : new CloudRead(null, KiffErrors.Words(w, status), status);
                }
                catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException) { return CloudRead.Unreached(w); }
            },
            (path, body) => Tafs.Core.NeedsYou.HoldAllow.SignConfirmAsync(session,
                session.PasskeyId is { } id ? new PasskeyApprover(new NamedHello(), KiffSigning.FromBase64Url(id), Features.WindowsPasskeyApprovals) : null,
                path, body, SigningTime.Next(DateTimeOffset.UtcNow)),
            (path, body, signature) => KiffHttp.PostAsync(new Uri(kiff, path), body, bearer, w.Language, signature: signature),
            async path =>
            {
                var at = SigningTime.Next(DateTimeOffset.UtcNow);
                using var key = TpmKey.Open(session.KeyName);
                var header = KiffSigning.SignatureHeaderV1(at, await key.SignDerAsync(KiffSigning.RequestV1(session.Tenant, "DELETE", path, [], at)));
                return await KiffHttp.DeleteAsync(new Uri(kiff, path), bearer, w.Language, signature: ("KIFF-Device-Signature", header));
            },
            () => reading == sessions, SessionStore.Why);
    }
}

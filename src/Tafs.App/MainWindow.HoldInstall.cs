using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// Coach's held install, from Needs you (kiff/tafs#206), as the iPad's <c>beginCoachInstall</c> and <c>useVersion(approving:)</c>:
/// approving it is installing it.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// "Install with Windows Hello" on Coach's held install:
    /// <list type="bullet">
    /// <item>its taf found by tafs cloud's id, else by name (<see cref="CoachInstalls.Find"/>); none says so;</item>
    /// <item>its versions read: a version it doesn't have says so; one already live is approved once at once, which only
    /// tells Coach so;</item>
    /// <item>otherwise the taf's page opens on its versions and installs it there, as Install this version does, with what's in
    /// the way stopped and its Card read first when it brings one; only once the version is live is the hold approved, with
    /// its own approval.</item>
    /// </list>
    /// Someone who can't install it here (not managing the taf, or a PC whose passkey doesn't sign) is told to answer in KIFF.
    /// </summary>
    async Task InstallFromHoldAsync(HeldCall call)
    {
        var w = App.Words;
        if (call.Install is not { } ask || home is null || cloudRead is not { } read) return;
        if (CoachInstalls.Find(ask, home.Tafs) is not { } taf)
        {
            needsYouError = w.Get("version.coach_unknown_taf");
            ShowSteps();
            return;
        }
        if (InstallingFor(taf) is null)
        {
            needsYouError = w.Get("needs_you.answer_in_kiff");
            ShowSteps();
            return;
        }
        var reading = sessions;
        var answer = await read(TafVersions.Path(taf.Id));
        if (reading != sessions) return;
        if (answer.Answer is not { } json || TafVersions.Read(json) is not { } list)
        {
            needsYouError = answer.Why ?? w.Get("kiff.kiff_did_not_answer_expected");
            ShowSteps();
            return;
        }
        if (list.Versions.FirstOrDefault(v => v.N == ask.N) is not { } version)
        {
            needsYouError = w.Get("version.no_such", ("taf", taf.Name), ("n", ask.N.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            ShowSteps();
            return;
        }
        // Live already: approving only tells Coach so.
        if (version.Live)
        {
            await AnswerHeldAsync(call, new HeldAnswer.ApproveOnce(), installed: true);
            return;
        }
        var opening = OpenTaf(w, taf.Id, TafTab.Card) with
        {
            CardOn = CardScreen.Versions,
            HoldInstall = new TafHoldInstall(ask.N, async () =>
            {
                // Live now: the hold approved once, its own approval, while it still waits.
                if (reading == sessions && held.Any(h => h.KiffId == call.KiffId)) await AnswerHeldAsync(call, new HeldAnswer.ApproveOnce(), installed: true);
            }),
        };
        Pages.Navigate(typeof(TafPage), opening);
    }
}

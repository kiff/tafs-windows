using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>
/// Resuming a halt, as the iPad's <c>resumeHalt</c>: it gives authority back, so KIFF takes it only signed by this PC's
/// approval key (<c>grantsAuthority</c>), which on Windows is its passkey under Windows Hello. KIFF first
/// (<c>POST /v1/agents/{agent}/resume</c>, or <c>/v1/resume</c> for every taf), then tafs cloud, which lets the taf run only
/// once KIFF says it's resumed (<c>DELETE</c> on the halt). What it was doing before the halt doesn't come back.
/// </summary>
public static class HaltResume
{
    /// <summary>KIFF's resume: one agent's, or with none every taf of the account.</summary>
    public static string KiffPath(string? agent) => agent is null ? "/v1/resume" : $"/v1/agents/{CloudRead.PathPart(agent)}/resume";

    /// <summary>The resume's body, an empty object as the iPad sends it.</summary>
    public static byte[] Body() => "{}"u8.ToArray();

    /// <summary>
    /// Whether this PC resumes: only a passkey session that knows its passkey (<see cref="HeldAnswers.Approves"/>), the same
    /// rule as Approve once. Any other session reads why it can't, and approves on another device.
    /// </summary>
    public static bool MayResume(KeptSession? session) => session is not null && HeldAnswers.Approves(session.Mode, session.PasskeyId);

    /// <summary>
    /// The resume's one signature: this PC's passkey asserts <c>kiff.request.v1</c> over exactly this request, behind one
    /// Windows Hello, as Start's Setup draft. A session that doesn't resume throws before anything is asked.
    /// </summary>
    public static async Task<(string Header, string Value)> SignAsync(KeptSession session, PasskeyApprover? passkey, string path,
        string signedAt, CancellationToken cancel = default)
    {
        if (!MayResume(session) || passkey is null) throw new PasskeyRefusedException("only this PC's passkey resumes");
        return (PasskeyApprover.HeaderName, await passkey.ApproveAsync(session.Tenant, "POST", path, Body(), signedAt, cancel));
    }

    /// <summary>
    /// Resume, in the iPad's order. <paramref name="taf"/> and <paramref name="agent"/> are the taf's tafs cloud id and KIFF
    /// agent, or none for every taf. Windows Hello closed sends nothing ("Cancelled, so nothing was approved…"); any other
    /// failure to sign sends nothing ("Not resumed: it wasn't confirmed."). KIFF not taking it says so in the app's words for
    /// its status; tafs cloud not taking it after KIFF did says KIFF resumed it, but tafs cloud didn't yet. A sign-out at any
    /// point ends it quietly, sending nothing more. Resumed, the halt as tafs cloud reads it now.
    /// </summary>
    public static async Task<(Halt? Halt, string? Why)> ResumeAsync(WordBook w, string? taf, string? agent, string? name,
        Func<string, Task<(string Header, string Value)>> sign,
        Func<string, (string Header, string Value), Task<int>> kiffPost,
        Func<string, Task<CloudRead>> cloudRelease,
        Func<bool> current)
    {
        // A taf with no KIFF agent can't be resumed at KIFF; nothing is asked.
        if (taf is not null && string.IsNullOrEmpty(agent)) return (null, w.Get("taf.halt.not_resumed_unconfirmed"));
        var path = KiffPath(taf is null ? null : agent);
        (string Header, string Value) signature;
        try { signature = await sign(path); }
        catch (OperationCanceledException) { return (null, current() ? w.Get("approval.cancelled_nothing_approved_request_kept") : null); }
        catch (Exception e) when (e is PasskeyRefusedException or InvalidDataException or COMException or CryptographicException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            return (null, current() ? w.Get("taf.halt.not_resumed_unconfirmed") : null);
        }
        if (!current()) return (null, null);

        int status;
        try { status = await kiffPost(path, signature); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { status = 0; }
        if (!current()) return (null, null);
        if (status is < 200 or > 299)
        {
            var why = KiffErrors.Words(w, status);
            return (null, name is null ? w.Get("taf.halt.kiff_not_resumed_all", ("why", why)) : w.Get("taf.halt.kiff_not_resumed", ("taf", name), ("why", why)));
        }

        var released = await cloudRelease(Halt.Path(taf));
        if (!current()) return (null, null);
        if (released.Answer is { } answer && Halt.Read(answer) is { } halt) return (halt, null);
        var cloudWhy = released.Why ?? w.Get("error.ours");
        return (null, name is null ? w.Get("taf.halt.cloud_not_resumed_all", ("why", cloudWhy))
            : w.Get("taf.halt.cloud_not_resumed", ("taf", name), ("why", cloudWhy)));
    }
}

using Tafs.Core.NeedsYou;
using Tafs.Core.Words;

namespace Tafs.Core.Approvals;

/// <summary>
/// What stands in an approval's place while Windows Hello can't confirm here, as the iPad's <c>ApprovalGate</c> and
/// <c>ApprovalReadiness.short</c>: the person learns why before the prompt, not from a failure after it.
/// </summary>
public static class HelloGate
{
    /// <summary>What every approval on this PC asks for, in each language: Windows' own name for it.</summary>
    public const string Hello = "Windows Hello";

    /// <summary>
    /// Ready: none, and the approval's button shows. Otherwise its line, in the shared words with Windows Hello for the iPad's
    /// Face ID or Touch ID:
    /// <list type="bullet">
    /// <item>not set up: "Approving needs Windows Hello, and none is set up. Set it up in …", with Windows' sign-in options as
    /// the place once the shared words name it (until then the line Your step says), Open Settings (it opens that very page,
    /// which tafs on Windows can) and Check again;</item>
    /// <item>busy: "Windows Hello can't be used right now. Try again in a moment.", with Check again;</item>
    /// <item>none it can use: "This device can't approve. Approve on another device signed in to your account.", alone.</item>
    /// </list>
    /// </summary>
    public static HelloNotReady? NotReady(WordBook w, HelloReadiness hello) => hello switch
    {
        HelloReadiness.Ready => null,
        HelloReadiness.NotSetUp => new(w.Has("approval.settings_path.windows")
                ? w.Get("approval.approving_needs_none_set_up", ("either", Hello), ("kind", w.Get("approval.settings_path.windows")))
                : w.Get("needs_you.your_step.set_up_hello"),
            OpensSettings: true, ChecksAgain: true),
        HelloReadiness.Busy => new(w.Get("approval.can_not_used_right_try", ("either", Hello)), OpensSettings: false, ChecksAgain: true),
        _ => new(w.Get("approval.device_can_not_approve_approve"), OpensSettings: false, ChecksAgain: false),
    };
}

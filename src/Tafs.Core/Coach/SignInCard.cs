using Tafs.Core.Kiff;
using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>
/// Coach's sign-in card, as the iPad's <c>SetupChatView.signIn</c>: the app by name, where it stands, and its words. Opening the
/// vendor's sign-in comes with connecting apps on Windows: until then its button is drawn off, and Not now answers it.
/// </summary>
public static class SignInCard
{
    /// <summary>
    /// Where it stands, as the iPad's <c>setupCardState</c> for a sign-in: done once answered (signed in), or once planque's
    /// grant is connected or holds the sign-in (<c>listed</c>); declined with Not now; failed, with why; a grant planque
    /// failed or disconnected, with planque's reason; else it needs the person.
    /// </summary>
    public static (ApprovalState State, string? Why) State(CoachThread thread, CoachCard c, PlanqueGrant? grant)
    {
        var answer = thread.AnswerTo(c.Id);
        if (answer?.Text is "declined" or "not now") return (ApprovalState.Declined, null);
        if (answer is { Failed: true } f) return (ApprovalState.Failed, f.Text.StartsWith("failed: ", StringComparison.Ordinal) ? f.Text["failed: ".Length..] : null);
        if (answer is not null || grant is { Connected: true } or { Listed: true }) return (ApprovalState.Done, null);
        if (grant is { Status: "failed" or "disconnected" }) return (ApprovalState.Failed, grant.Reason);
        return (ApprovalState.NeedsYou, null);
    }

    /// <summary>The app it names, as the iPad's <c>signInApp</c>: the card's service, else the connection's app as planque names it.</summary>
    public static string? App(CoachCard c, PlanqueGrant? grant) =>
        new[] { c.Service, grant?.VendorName }.Select(s => s?.Trim()).FirstOrDefault(s => !string.IsNullOrEmpty(s));

    /// <summary>"Sign in at Gmail", and "Sign in at the vendor" only when no app is known; a signed-out connection, planque's words.</summary>
    public static string Title(WordBook w, CoachCard c, PlanqueGrant? grant) =>
        grant is { NeedsReconnect: true, StatusWords: { Length: > 0 } words } ? words
        : App(c, grant) is { } app ? w.Get("coach.sign_in.at", ("vendor", app)) : w.Get("coach.sign_in.at_vendor");

    /// <summary>Its button: Try again after a failure, Sign in again for a signed-out connection, else Coach's words or "Open Gmail".</summary>
    public static string Button(WordBook w, CoachCard c, PlanqueGrant? grant, bool failed) =>
        failed ? w.Get("common.try_again")
        : grant is { NeedsReconnect: true } ? w.Get("connect.sign_in_again")
        : c.Yes ?? (c.Service is { } service ? w.Get("coach.sign_in.open", ("service", service)) : w.Get("coach.sign_in.open_sign_in"));

    /// <summary>Where it goes, always the app's own words, never Coach's: the card's service, else the vendor's page.</summary>
    public static string Opens(WordBook w, CoachCard c) =>
        w.Get("coach.sign_in.opens", ("destination", c.Service ?? w.Get("connect.the_vendors_page")));

    /// <summary>Answered, one line: "Signed in at Gmail", or "Not now: no sign-in at Gmail".</summary>
    public static string Outcome(WordBook w, CoachCard c, PlanqueGrant? grant, ApprovalState state)
    {
        var vendor = App(c, grant) ?? w.Get("coach.the_vendor");
        return state == ApprovalState.Done ? w.Get("coach.answer.signed_in_at", ("vendor", vendor)) : w.Get("coach.not_no_sign", ("vendor", vendor));
    }
}

using System.Text.Json;
using Tafs.Core.Kiff;

namespace Tafs.Core.Taf;

/// <summary>
/// What tafs cloud says to do next about an Activity line (<c>next</c>): its kind (<c>reconnect</c>, <c>ask_coach</c>, or one this
/// app doesn't know), its words, and the tool, taf or message it names.
/// </summary>
public sealed record NextStep(string Kind, string Words, string? Tool = null, string? Taf = null, string? Message = null);

/// <summary>What a next step does, as the iPad's <c>NextStepWords.Action</c>.</summary>
public abstract record NextStepAction
{
    /// <summary>The connection's vendor signed it out: sign in again, as on its taf's Setup.</summary>
    public sealed record SignInAgain(PlanqueGrant Grant) : NextStepAction;
    /// <summary>Connect apps, where the owner connects that app again (a refused key is replaced there).</summary>
    public sealed record ConnectApps : NextStepAction;
    /// <summary>Coach on the taf, with these words in the box for the owner to send.</summary>
    public sealed record AskCoach(string Words) : NextStepAction;
    /// <summary>A line: try later, or a kind this app doesn't know.</summary>
    public sealed record Line : NextStepAction;
}

/// <summary>A next step read and turned into what it does, as the iPad's <c>NextStepWords</c> and <c>NextStepRow</c>.</summary>
public static class NextSteps
{
    /// <summary>An entry's <c>next</c>: none without its kind or words, as the iPad's decoder needs both.</summary>
    public static NextStep? Read(JsonElement entry) =>
        Lenient.Field(entry, "next") is { ValueKind: JsonValueKind.Object } n && Lenient.Text(n, "kind") is { } kind && Lenient.Text(n, "words") is { } words
            ? new NextStep(kind, words, Lenient.Text(n, "tool"), Lenient.Text(n, "taf"), Lenient.Text(n, "message"))
            : null;

    /// <summary>
    /// Whether a next step is a button here, as the iPad's <c>NextStepRow.may</c>: for whoever manages the taf, a reconnect
    /// always (it signs in again or opens Connect apps), Ask Coach for someone who may use Coach; anything else, and anyone
    /// else, reads it as a line.
    /// </summary>
    public static bool Offered(NextStep next, bool manages, bool mayUseCoach) =>
        manages && next.Kind switch { "reconnect" => true, "ask_coach" => mayUseCoach, _ => false };

    /// <summary>
    /// The connection whose tools include <paramref name="tool"/>: the taf's own first (its KIFF agent), else any of the
    /// account's. One disconnected or failed is none.
    /// </summary>
    public static PlanqueGrant? Connection(string? tool, string? agent, IReadOnlyList<PlanqueGrant> grants)
    {
        if (string.IsNullOrEmpty(tool)) return null;
        var live = grants.Where(g => g.Status is not ("disconnected" or "failed") && Actions(g).Contains(tool)).ToList();
        return live.FirstOrDefault(g => g.Request.Agent == agent) ?? live.FirstOrDefault();
    }

    /// <summary>A grant's tools as KIFF names them: planque's own list, else its request's tools the taf uses.</summary>
    static IReadOnlySet<string> Actions(PlanqueGrant g) =>
        (g.Tools ?? g.Request.Tools.Where(t => t.Tafs).Select(t => t.Action).ToList()).ToHashSet();

    /// <summary>
    /// What <paramref name="next"/> does for the taf whose KIFF agent is <paramref name="agent"/>, as the iPad's
    /// <c>NextStepWords.action</c>: a reconnect signs in again the connection that needs it, else opens Connect apps; Ask Coach
    /// puts tafs cloud's message in Coach's box, else <paramref name="fallback"/> (the line's title and detail); anything else
    /// is a line.
    /// </summary>
    public static NextStepAction Action(NextStep next, string? agent, IReadOnlyList<PlanqueGrant> grants, string fallback) => next.Kind switch
    {
        "reconnect" => Connection(next.Tool, agent, grants) is { NeedsReconnect: true } g ? new NextStepAction.SignInAgain(g) : new NextStepAction.ConnectApps(),
        "ask_coach" => new NextStepAction.AskCoach(next.Message?.Trim() is { Length: > 0 } ? next.Message! : fallback),
        _ => new NextStepAction.Line(),
    };
}

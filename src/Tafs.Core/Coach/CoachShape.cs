using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.Words;

namespace Tafs.Core.Coach;

public enum ShapeStepState { Done, NeedsYou, Now, ToDo, Failed, Skipped }

/// <summary>A step of the plan, or what Coach asked of the person, and where it stands.</summary>
public sealed record ShapeStep(string Label, ShapeStepState State);

public enum ShapeToolState { Connected, SignedIn, Waiting, Failed }

/// <summary>A vendor the conversation connects, from planque's grant: where it stands, why it failed, and its tools once connected.</summary>
public sealed record ShapeTool(string Id, string Vendor, ShapeToolState State, string? Why, IReadOnlyList<string> Actions);

/// <summary>The taf's Card as KIFF drafted it: a draft until the person approves it.</summary>
public sealed record ShapeCard(string Title, IReadOnlyList<string> Lines, bool Confirmed);

/// <summary>
/// What Coach's conversation has made so far, for "Taking shape" beside it on a wide panel (kiff/tafs#85), as the iPad's
/// <c>SetupShape</c>: the taf's name, the plan, the tools it connects and the Card KIFF drafted. Read only from what this PC
/// already holds (the thread, planque's grants and KIFF's drafts); nothing here asks a server for more.
/// </summary>
public sealed record CoachShape(string? Name, string? Title, IReadOnlyList<ShapeStep> Steps, IReadOnlyList<ShapeTool> Tools, ShapeCard? Card)
{
    public bool IsEmpty => Name is null && Steps.Count == 0 && Tools.Count == 0 && Card is null;

    /// <summary>The cards that ask the person something, in the order they came: what the plan is when Coach shows none.</summary>
    static readonly string[] Asks = ["approval", "sign_in", "questions", "confirm"];

    public static CoachShape Of(WordBook w, CoachThread thread, IReadOnlyList<PlanqueGrant> grants, IReadOnlyDictionary<string, AgentDraft> drafts)
    {
        var shown = thread.Shown;
        var name = shown.LastOrDefault(c => c.Card.Kind == "result" && c.Card.Taf is not null)?.Card.Taf?.Name;
        // The card waiting for the person: the latest that waits on them, not answered.
        var waiting = shown.LastOrDefault(c => c.Card.Waits == "owner" && c.Answer is null && Asks.Contains(c.Card.Kind));

        string? title = null;
        List<ShapeStep> steps;
        if (thread.Plan is { } plan)
        {
            // The plan, as Coach keeps it. Its step at work waits for the person when a card does.
            title = plan.Card.Title;
            var waited = false;
            steps = (plan.Card.Steps ?? []).Select(step =>
            {
                switch (step.Status)
                {
                    case "done": return new ShapeStep(step.Label, ShapeStepState.Done);
                    case "failed": return new ShapeStep(step.Label, ShapeStepState.Failed);
                    case "skipped": return new ShapeStep(step.Label, ShapeStepState.Skipped);
                    case "active":
                        var needsYou = waiting is not null && !waited;
                        waited |= needsYou;
                        return new ShapeStep(step.Label, needsYou ? ShapeStepState.NeedsYou : ShapeStepState.Now);
                    default: return new ShapeStep(step.Label, ShapeStepState.ToDo);
                }
            }).ToList();
        }
        else
        {
            // No plan shown: what Coach asked of the person, and where each stands.
            steps = shown.Where(c => Asks.Contains(c.Card.Kind)).Select(ask =>
            {
                var c = ask.Card;
                var grant = grants.FirstOrDefault(g => g.Id == c.Grant);
                var vendor = grant?.Vendor ?? c.Service;
                var label = c.Kind switch
                {
                    "approval" => w.Get("coach.shape.approve_named", ("vendor", vendor ?? w.Get("coach.shape.the_connection"))),
                    "sign_in" => vendor is not null ? w.Get("coach.sign_in.at", ("vendor", vendor)) : w.Get("coach.sign_in.at_vendor"),
                    _ => c.Text ?? c.Title ?? w.Get("needs_you.answer"),
                };
                if (ReferenceEquals(ask, waiting)) return new ShapeStep(label, ShapeStepState.NeedsYou);
                var answered = thread.AnswerTo(c.Id) is not null || ask.Answer is not null;
                // A confirm answered, or written past: the conversation went on without it.
                if (c.Kind == "confirm") return new ShapeStep(label, answered ? ShapeStepState.Done : ShapeStepState.Skipped);
                if (c.Kind is "approval" or "sign_in")
                {
                    var (state, _) = c.Kind == "approval" ? ApprovalCard.State(thread, c, grant) : SignInCard.State(thread, c, grant);
                    return new ShapeStep(label, state switch
                    {
                        ApprovalState.Done => ShapeStepState.Done,
                        ApprovalState.Declined => ShapeStepState.Skipped,
                        ApprovalState.Failed => ShapeStepState.Failed,
                        _ => ShapeStepState.ToDo,
                    });
                }
                return new ShapeStep(label, answered ? ShapeStepState.Done : ShapeStepState.ToDo);
            }).ToList();
        }

        // The grants the conversation's cards name, in the order they came, each once.
        var seen = new HashSet<string>();
        var tools = new List<ShapeTool>();
        foreach (var c in shown.Select(s => s.Card))
        {
            if (c.Grant is not { } id || !seen.Add(id) || grants.FirstOrDefault(g => g.Id == id) is not { } grant) continue;
            var state = grant.Connected ? ShapeToolState.Connected
                : grant.Status is "failed" or "disconnected" ? ShapeToolState.Failed
                : grant.HoldsSignIn ? ShapeToolState.SignedIn
                : ShapeToolState.Waiting;
            tools.Add(new ShapeTool(id, grant.Vendor, state, state == ShapeToolState.Failed ? grant.Reason : null,
                grant.Connected ? grant.Request.Tools.Select(t => t.Title ?? t.Name ?? t.Tool).ToList() : []));
        }

        // The latest draft an approval names that KIFF has shown this PC, with tools.
        var draft = shown.Where(s => s.Card.Kind == "approval").Select(s => s.Card.Draft is { } d ? drafts.GetValueOrDefault(d) : null)
            .LastOrDefault(d => d is not null && d.NoTools != true);
        var card = draft is null ? null : new ShapeCard(draft.CardTitle(w), draft.CardLines(w).SelectMany(l => l).ToList(), draft.Confirmed);
        return new(name, title, steps, tools, card);
    }
}

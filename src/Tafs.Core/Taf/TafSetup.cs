using System.Globalization;
using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>One action a connection gives the taf: its name, its title, and its limits in KIFF's words.</summary>
public sealed record SetupAction(string Name, string Title, IReadOnlyList<string> Limits);

/// <summary>
/// One connected app, as tafs cloud says it: its status in words ("Sign in again") and whether that means signing in
/// again, its actions (and how many), the last seven days in words with the query that opens them on the Activity, and
/// whether a step of its workflow is running on it now.
/// </summary>
public sealed record SetupConnection(string Id, string Name, string Status, string StatusWords, bool Reconnect,
    string ActionWords, IReadOnlyList<SetupAction> Actions, string? WorkflowWords, string StatsWords, string? ActivityQuery,
    int ActionCount = 0, bool Running = false);

/// <summary>One app around the taf in Setup's ring: the connection that does the work, with any other sign-in to the same app.</summary>
public sealed record SetupNode(SetupConnection App, IReadOnlyList<SetupConnection> Others);

/// <summary>
/// A taf's Setup as last read, as the iPad keeps <c>tafSetups</c> and <c>tafSetupErrors</c>: the page, kept when a later
/// read fails, and why that read failed. Neither yet: it's being read.
/// </summary>
public sealed class SetupRead
{
    public SetupPage? Page { get; private set; }
    public string? Error { get; private set; }

    /// <summary>The page is drawn from the quick read while the signed one, which reads planque's connections, is still on its way.</summary>
    public bool ConnectionsPending { get; private set; }

    /// <summary>
    /// <c>GET /v1/tafs/{id}/setup</c>, as the iPad's <c>loadTafSetup</c>: with planque's signed grants read
    /// (<c>Planque-Device-Signature</c>) when <paramref name="proof"/> gives one, and without it when it gives none or fails,
    /// as the iPad's <c>try?</c>. <paramref name="read"/> takes the path and the proof, none when there's none.
    /// With a proof to make, the page doesn't wait for planque: a quick read without it goes at the same time, and draws the
    /// job, the plan, what it remembers and the asks at once (<paramref name="drawn"/>), keeping the connections already shown
    /// while the signed read brings them; that one's answer replaces it. A read that fails keeps the page read before.
    /// </summary>
    public async Task LoadAsync(string taf, Func<string, string?, Task<CloudRead>> read, Func<Task<string?>>? proof = null, Action? drawn = null,
        string? connectedAgain = null)
    {
        var path = TafSetup.Path(taf, connectedAgain);
        if (proof is null)
        {
            Take(await read(path, null));
            return;
        }
        ConnectionsPending = true;
        var quick = read(path, null);
        var signed = SignedAsync(path, read, proof, quick);
        var first = await Task.WhenAny(quick, signed);
        if (first == quick && !signed.IsCompleted && quick.Result.Answer is { } json)
        {
            var page = TafSetup.Read(json);
            // The connections need planque's proof: those already shown stay until the signed answer replaces them.
            Page = page with { Connections = Page?.Connections ?? [], ConnectionsError = Page?.ConnectionsError, ConnectAgain = Page?.ConnectAgain };
            Error = null;
            drawn?.Invoke();
        }
        var answer = await signed;
        ConnectionsPending = false;
        Take(answer);
    }

    /// <summary>The signed read; when no proof could be made, it's the quick read itself, never the same read twice.</summary>
    static async Task<CloudRead> SignedAsync(string path, Func<string, string?, Task<CloudRead>> read, Func<Task<string?>> proof, Task<CloudRead> quick)
    {
        string? signature = null;
        try { signature = await proof(); }
        catch (Exception e) when (e is not OperationCanceledException) { }
        return signature is null ? await quick : await read(path, signature);
    }

    void Take(CloudRead answer)
    {
        if (answer.Answer is { } json)
        {
            Page = TafSetup.Read(json);
            Error = null;
        }
        else Error = answer.Why;
    }
}

/// <summary>A step of the taf's workflow: its number, label and words, the app it uses, and whether it's running.</summary>
public sealed record WorkflowStep(int Number, string Label, string Words, string? App, bool Running);

public sealed record SetupWorkflow(string Title, string Summary, IReadOnlyList<WorkflowStep> Steps);

/// <summary>What it remembers, in short: "3 notes", and the latest when there is one.</summary>
public sealed record SetupMemory(int Count, string Title, string? Latest);

/// <summary>Its versions, in short: "Changes to how it runs", "Runs its original definition".</summary>
public sealed record SetupVersions(string Title, string Summary, int? Live, int Earlier);

/// <summary>
/// A call its Card couldn't count, in tafs cloud's words ("Rae needs a USD limit"): what happened, how often, and what
/// to do. Approving the change goes through Coach and is signed there; nothing here approves.
/// </summary>
public sealed record CardUnitException(string Id, string Title, string Words, string History, string ActionTitle,
    string DismissTitle, string CoachText, bool CanApproveOnce, string Taf = "", string Name = "");

/// <summary>
/// The taf's own Connect again: the connection whose sign-in expired, and tafs cloud's words for it and after it.
/// <paramref name="Fresh"/>: planque can't sign it in again on the same account, so a new sign-in (<c>connect-fresh</c>).
/// </summary>
public sealed record ConnectAgain(string Grant, string App, string Title, string Words, string DoneWords, bool Fresh = false)
{
    /// <summary>
    /// What the page says once it's connected again, as the iPad's <c>connectAgain</c>: after a fresh sign-in, tafs cloud's
    /// words for this connection read with <c>?connected_again=</c> (which account it went to, which tafs use it now); else its
    /// <c>done_words</c>. None when it didn't connect, or tafs cloud has no words for it.
    /// </summary>
    public string? Done(bool connected, ConnectedAgain? named)
    {
        if (!connected) return null;
        if (Fresh && named is { } n && n.Grant == Grant && n.Words.Length > 0) return n.Words;
        return DoneWords.Length > 0 ? DoneWords : null;
    }
}

/// <summary>Read after a fresh sign-in (<c>?connected_again=</c>): the connection, and tafs cloud's words for where it went.</summary>
public sealed record ConnectedAgain(string Grant, string Words);

/// <summary>
/// A taf's Setup, as tafs cloud's <c>GET /v1/tafs/{id}/setup</c> answers whoever manages it (contracts/taf-setup*.json):
/// its connections (or why they couldn't be read), workflow, memory and versions, the Card-unit exceptions waiting, and
/// its own Connect again.
/// </summary>
public sealed record SetupPage(IReadOnlyList<SetupConnection> Connections, string? ConnectionsError, SetupWorkflow Workflow,
    SetupMemory Memory, SetupVersions Versions, IReadOnlyList<CardUnitException> CardUnitExceptions, ConnectAgain? ConnectAgain,
    ConnectedAgain? ConnectedAgain = null);

/// <summary>A note the taf keeps, as tafs cloud's memory: its file's path, what it says, and when it changed.</summary>
public sealed record MemoryNote(string Id, string Path, string Content, DateTimeOffset UpdatedAt);

/// <summary>Reads a taf's Setup, as the iPad's SetupView: what can't be read reads as none, never the page with it.</summary>
public static class TafSetup
{
    public static SetupPage Read(JsonElement answer)
    {
        var workflow = Lenient.Field(answer, "workflow") ?? default;
        var memory = Lenient.Field(answer, "memory") ?? default;
        var versions = Lenient.Field(answer, "versions") ?? default;
        return new SetupPage(
            Lenient.Array(answer, "connections").Select(Connection).OfType<SetupConnection>().ToList(),
            Lenient.Text(answer, "connections_error"),
            new SetupWorkflow(Lenient.Text(workflow, "title") ?? "", Lenient.Text(workflow, "summary") ?? "",
                Lenient.Array(workflow, "steps").Select(Step).OfType<WorkflowStep>().ToList()),
            new SetupMemory(Lenient.Int(memory, "count") ?? 0, Lenient.Text(memory, "title") ?? "", Lenient.Text(memory, "latest")),
            new SetupVersions(Lenient.Text(versions, "title") ?? "", Lenient.Text(versions, "summary") ?? "",
                Lenient.Int(versions, "live"), Lenient.Int(versions, "earlier") ?? 0),
            Lenient.Array(answer, "card_unit_exceptions").Select(Exception).OfType<CardUnitException>().ToList(),
            Again(Lenient.Field(answer, "connect_again")),
            Lenient.Field(answer, "connected_again") is { } done && Lenient.Text(done, "grant") is { } grant
                ? new ConnectedAgain(grant, Lenient.Text(done, "words")?.Trim() ?? "") : null);
    }

    /// <summary>A connection with no id or name is left out.</summary>
    static SetupConnection? Connection(JsonElement c)
    {
        if (Lenient.Text(c, "id") is not { } id || Lenient.Text(c, "name") is not { } name) return null;
        var stats = Lenient.Field(c, "stats") ?? default;
        return new SetupConnection(id, name, Lenient.Text(c, "status") ?? "", Lenient.Text(c, "status_words") ?? "",
            Lenient.Bool(c, "reconnect"), Lenient.Text(c, "action_words") ?? "",
            Lenient.Array(c, "actions").Select(Action).OfType<SetupAction>().ToList(),
            Lenient.Text(c, "workflow_words"), Lenient.Text(stats, "words") ?? "", Lenient.Text(stats, "activity_query"),
            Lenient.Int(c, "action_count") ?? 0, Lenient.Bool(c, "running"));
    }

    /// <summary><c>GET /v1/tafs/{id}/setup</c>; after a fresh sign-in, with <c>?connected_again=</c> that connection.</summary>
    public static string Path(string taf, string? connectedAgain = null) => $"/v1/tafs/{CloudRead.PathPart(taf)}/setup"
        + (connectedAgain is { Length: > 0 } grant ? "?connected_again=" + Uri.EscapeDataString(grant) : "");

    /// <summary>
    /// The ring's apps, as the iPad's <c>ConnectedToolsRing.nodes</c>: one an app, in tafs cloud's order of each app's first
    /// connection. The one shown is the first with actions, else one to sign in again, else the first; the others go inside
    /// it. Two "Stripe" connections, one choosing actions and one with two, read as one Stripe.
    /// </summary>
    public static IReadOnlyList<SetupNode> Nodes(IReadOnlyList<SetupConnection> apps) =>
        apps.GroupBy(a => a.Name).Select(g =>
        {
            var all = g.ToList();
            var main = all.FirstOrDefault(a => a.ActionCount > 0) ?? all.FirstOrDefault(a => a.Reconnect) ?? all[0];
            return new SetupNode(main, all.Where(a => a.Id != main.Id).ToList());
        }).ToList();

    static SetupAction? Action(JsonElement a) =>
        Lenient.Text(a, "name") is { } name
            ? new SetupAction(name, Lenient.Text(a, "title") ?? name,
                Lenient.Array(a, "limits").Where(l => l.ValueKind == JsonValueKind.String).Select(l => l.GetString()!).ToList())
            : null;

    static WorkflowStep? Step(JsonElement s) =>
        Lenient.Int(s, "number") is { } n && Lenient.Text(s, "label") is { } label
            ? new WorkflowStep(n, label, Lenient.Text(s, "words") ?? "", Lenient.Text(s, "app"), Lenient.Bool(s, "running"))
            : null;

    /// <summary>An exception with no id, title or words is left out: there'd be nothing to say. Its taf is tafs cloud's id for it.</summary>
    public static CardUnitException? Exception(JsonElement e) =>
        Lenient.Text(e, "id") is { } id && Lenient.Text(e, "title") is { } title && Lenient.Text(e, "words") is { } words
            ? new CardUnitException(id, title, words, Lenient.Text(e, "history") ?? "", Lenient.Text(e, "action_title") ?? "",
                Lenient.Text(e, "dismiss_title") ?? "", Lenient.Text(e, "coach_text") ?? "", Lenient.Bool(e, "can_approve_once"),
                Lenient.Text(e, "taf") ?? "", Lenient.Text(e, "name") ?? "")
            : null;

    /// <summary>As the iPad's ConnectAgain.usable: one without its connection, title or words reads as none.</summary>
    static ConnectAgain? Again(JsonElement? a)
    {
        if (a is not { } o) return null;
        string T(string name) => Lenient.Text(o, name)?.Trim() ?? "";
        var again = new ConnectAgain(T("grant"), T("app"), T("title"), T("words"), T("done_words"), Lenient.Bool(o, "fresh"));
        return again.Grant.Length > 0 && again.Title.Length > 0 && again.Words.Length > 0 ? again : null;
    }

    /// <summary>A note's line under it: its path as words, then its time ("customers · regulars · 02:57").</summary>
    public static string NoteLine(MemoryNote note, TimeZoneInfo zone) =>
        string.Join(" · ", note.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => (part.EndsWith(".md", StringComparison.Ordinal) ? part[..^3] : part).Replace('-', ' '))
                .Append(TimeZoneInfo.ConvertTime(note.UpdatedAt, zone).ToString("HH:mm", CultureInfo.InvariantCulture)));

    /// <summary>The sample Support replies' job, as the reference set's taf-setup.png.</summary>
    public static string SampleJob(WordBook w) => w.Get("sample.support.job");

    /// <summary>
    /// The sample's notes, as the iPad's: the kind of thing a real taf writes down as it learns, an hour, a day and three
    /// days old, newest first.
    /// </summary>
    public static IReadOnlyList<MemoryNote> SampleNotes(WordBook w, DateTimeOffset now) =>
    [
        new("sample-0", "/how-you-like-it.md", w.Get("sample.memory.explain"), now.AddHours(-1)),
        new("sample-1", "/customers/regulars.md", w.Get("sample.memory.regulars"), now.AddDays(-1)),
        new("sample-2", "/from-the-owner.md", w.Get("sample.memory.ask_first"), now.AddDays(-3)),
    ];
}

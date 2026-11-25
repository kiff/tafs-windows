using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;

namespace Tafs.Core.Coach;

/// <summary>One Card in KIFF's own words, as the iPad's <c>CoachTools.CardDraft.Words</c>.</summary>
public sealed record CardWords(string Title, IReadOnlyList<string> Lines);

/// <summary>
/// One request tafs cloud built, as the iPad's <c>CoachStart.Request</c>: sent as <see cref="Body"/>'s bytes, exactly as
/// tafs cloud wrote them, to KIFF's API or its gateway; with a line per tool, or KIFF's words for the draft's Cards.
/// </summary>
public sealed record StartRequest(string Id, string Method, string Path, bool Gateway, byte[] Body, IReadOnlyList<string> Lines,
    string? CardTitle, IReadOnlyList<CardWords> Cards);

/// <summary>
/// A tool of Coach's, in tafs cloud's words, as the iPad's <c>CoachTools.Step.Tool</c>; and what its Card signs, as data
/// (<see cref="CardStepCheck"/>): the action, its Card's id, the Card's count a day, whether past it the call waits for the
/// person, and whether its Card is in the draft this same answer carries to sign. None of those from a tafs cloud that
/// doesn't send them.
/// </summary>
public sealed record StepTool(string Name, string Does, string Why, string? Tool = null, string? Card = null, int? Limit = null,
    bool? Asks = null, bool? InDraft = null);

/// <summary>A group of Coach's tools, as the iPad's <c>CoachTools.Step.Group</c>.</summary>
public sealed record StepGroup(string Name, string Line, IReadOnlyList<StepTool> Tools);

/// <summary>
/// Start's own words, as the iPad's <c>CoachStart.Words</c>: the line before it, its button, and what it does; and the Card
/// step's (tafs cloud's <c>start.words</c>): its title, what the approval allows, what always waits for the person, how this
/// PC signs (with its passkey: <c>sign_passkey</c>), how to stop it, the fold over what's signed, the button
/// (<c>buttons.passkey</c>), and the line in the box's place until it's approved (<c>locked</c>).
/// </summary>
public sealed record FirstStartWords(string Lead, string Start, string Does)
{
    public string Title { get; init; } = "";
    public string Allows { get; init; } = "";
    public string Asks { get; init; } = "";
    public string Sign { get; init; } = "";
    public string Stop { get; init; } = "";
    public string Exact { get; init; } = "";
    public string Give { get; init; } = "";
    public string Locked { get; init; } = "";

    /// <summary>Every line of the Card step came, this PC's button included: it's drawn only then, as the iPhone's <c>cardStep(face:)</c>.</summary>
    public bool CardStep => new[] { Title, Allows, Asks, Sign, Stop, Exact, Give }.All(s => s.Length > 0);

    /// <summary>
    /// The line in the box's place on the Card step: tafs cloud's <c>locked</c>, else the app's own
    /// (<c>coach.tools.write_after</c>). Never blank, so the box is never drawn open for want of words.
    /// </summary>
    public string LockedLine(Words.WordBook w) => Locked.Length > 0 ? Locked : w.Get("coach.tools.write_after");
}

/// <summary>
/// Coach's first start (<c>GET /v1/setup</c>'s <c>start</c>, before Coach exists), as the iPad's <c>TafsCloud.CoachStart</c>:
/// the tools batch for the gateway and Coach's draft with its Cards, signed once (<c>kiff.requests.v1</c>); or, with every
/// tool already connected, the draft alone (<c>kiff.request.v1</c>). This PC builds neither.
/// </summary>
public sealed record FirstStart(string State, string Scheme, IReadOnlyList<StartRequest> Requests, string Details,
    IReadOnlyList<StepGroup> Groups, FirstStartWords? Words, string AgentId = "")
{
    public StartRequest? Connect => Requests.FirstOrDefault(r => r.Id == "connect");
    public StartRequest? Draft => Requests.FirstOrDefault(r => r.Id == "draft");

    /// <summary>The pair, signed once: both requests and the scheme that names two.</summary>
    public bool Together => Scheme == RequestsTogether.Scheme && Connect is not null && Draft is not null;

    /// <summary>
    /// Something this PC can sign and send now, as the iPad's <c>startable</c>: the pair, or the draft alone, and never Cards
    /// the person hasn't read in KIFF's own words.
    /// </summary>
    public bool Startable
    {
        get
        {
            if (State != "to_give" || Draft is not { Method: "POST", Path: CoachStart.DraftPath, Gateway: false } draft) return false;
            if (draft.Cards.Count == 0) return false;
            if (Connect is { } connect) return Together && connect is { Method: "POST", Path: "/v1/connections/batch", Gateway: true };
            return Scheme == "kiff.request.v1";
        }
    }

    /// <summary>Start on its own screen, in tafs cloud's words, with what's signed drawn under them, as the iPad's <c>oneScreen</c>.</summary>
    public bool OneScreen => Startable && Words is not null;

    /// <summary>Whether <paramref name="read"/> asks to sign exactly what this showed: the same requests, byte for byte, with the same words for the Cards.</summary>
    public bool SignsAsShown(FirstStart read) => Requests.Count == read.Requests.Count
        && Requests.Zip(read.Requests).All(r => CoachTools.Same(r.First, r.Second));

    /// <summary>Start's screen is the Card step (kiff/tafs docs/coach.md): every line of it sent, for this PC's way of approving.</summary>
    public bool CardStep => OneScreen && Words!.CardStep;

    /// <summary>
    /// Whether the plain list the Card step shows is exactly what the draft signs (<see cref="CardStepCheck.Matches"/>), and the
    /// tools batch signed with it (when there is one) connects only the step's tools, as Coach's own control server at
    /// <paramref name="mcp"/>: false, and the Card step asks for no signature.
    /// </summary>
    public bool CardStepSigns(string mcp) => Draft is { } draft && CardStepCheck.Matches(Groups, draft.Body, AgentId)
        && (Connect is not { } connect || CardStepCheck.BatchMatches(Groups, connect.Body, mcp));

    /// <summary>Read from <c>GET /v1/setup</c>'s answer, as the iPad's <c>CoachStart.read</c>; none without <c>start</c>, or once Coach exists.</summary>
    public static FirstStart? Read(JsonElement setup)
    {
        if (Lenient.Field(setup, "setup") is { ValueKind: JsonValueKind.Object }) return null;
        if (Lenient.Field(setup, "start") is not { ValueKind: JsonValueKind.Object } start) return null;
        var requests = new List<StartRequest>();
        if (Lenient.Field(start, "requests") is { ValueKind: JsonValueKind.Array } list)
            foreach (var r in list.EnumerateArray())
                if (r.ValueKind == JsonValueKind.Object && Lenient.Text(r, "id") is { } id && Lenient.Text(r, "method") is { } method
                    && Lenient.Text(r, "path") is { } path && r.TryGetProperty("body", out var body))
                    requests.Add(new(id, method, path, r.TryGetProperty("gateway", out var g) && g.ValueKind == JsonValueKind.True,
                        Encoding.UTF8.GetBytes(body.GetRawText()), Texts(r, "lines"), Lenient.Text(r, "card_title"), ReadCards(r)));
        var step = Lenient.Field(start, "coach_tools_step") is { ValueKind: JsonValueKind.Object } s ? s : (JsonElement?)null;
        FirstStartWords? words = null;
        if (Lenient.Field(start, "words") is { ValueKind: JsonValueKind.Object } w)
        {
            string Said(string key) => (Lenient.Text(w, key) ?? "").Trim();
            string Line(string group, string key) =>
                (Lenient.Field(w, group) is { ValueKind: JsonValueKind.Object } g ? Lenient.Text(g, key) ?? "" : "").Trim();
            if (Said("lead").Length > 0 && Said("start").Length > 0)
                words = new(Said("lead"), Said("start"), Said("does"))
                {
                    Title = Said("title"), Allows = Line("lines", "allows"), Asks = Line("lines", "asks"),
                    // This PC signs with its passkey: the passkey's sign line and button, never the phone's.
                    Sign = Said("sign_passkey"), Stop = Line("lines", "stop"), Exact = Said("exact"),
                    Give = Line("buttons", "passkey"), Locked = Said("locked"),
                };
        }
        return new(Lenient.Text(start, "state") ?? "unknown", Lenient.Text(start, "scheme") ?? "", requests,
            step is { } st ? Lenient.Text(st, "details") ?? "" : "", step is { } st2 ? ReadGroups(st2) : [], words,
            Lenient.Text(start, "agent_id") ?? "");
    }

    static List<string> Texts(JsonElement e, string name) => Lenient.Field(e, name) is { ValueKind: JsonValueKind.Array } a
        ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : [];

    /// <summary>KIFF's words for the Cards: a title (or its card title or label) and lines; one with neither is left out.</summary>
    static List<CardWords> ReadCards(JsonElement r) => Lenient.Field(r, "cards") is { ValueKind: JsonValueKind.Array } a
        ? a.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Object)
            .Select(c => (Title: Lenient.Text(c, "title") ?? Lenient.Text(c, "card_title") ?? Lenient.Text(c, "label"), Lines: Texts(c, "lines")))
            .Where(c => c.Title is not null || c.Lines.Count > 0).Select(c => new CardWords(c.Title ?? "", c.Lines)).ToList()
        : [];

    /// <summary>A JSON boolean as it came; none for a missing one or anything else.</summary>
    static bool? Flag(JsonElement e, string name) => Lenient.Field(e, name)?.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    /// <summary>The step's groups, each with its tools; a tool with no name is left out.</summary>
    static List<StepGroup> ReadGroups(JsonElement step) => Lenient.Field(step, "groups") is { ValueKind: JsonValueKind.Array } a
        ? a.EnumerateArray().Where(g => g.ValueKind == JsonValueKind.Object).Select(g => new StepGroup(Lenient.Text(g, "name") ?? "",
            Lenient.Text(g, "line") ?? "", Lenient.Field(g, "tools") is { ValueKind: JsonValueKind.Array } tools
                ? tools.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.Object)
                    .Select(t => new StepTool(Lenient.Text(t, "name") ?? "", Lenient.Text(t, "does") ?? "", Lenient.Text(t, "why") ?? "",
                        Lenient.Text(t, "tool"), Lenient.Text(t, "card"), Lenient.Int(t, "limit"), Flag(t, "asks"), Flag(t, "in_draft")))
                    .Where(t => t.Name.Length > 0).ToList()
                : [])).ToList()
        : [];
}

/// <summary>
/// Two requests under one signature (<c>kiff.requests.v1</c>), as the iPad's <c>RequestsTogether</c>: both built in full with
/// one <c>signed_at</c>, signed once by the approval key over the two request messages' hashes. Each service rebuilds its own
/// request's message, takes the other's hash as given (<c>with</c>), and checks the one signature.
/// </summary>
public static class RequestsTogether
{
    public const string Scheme = "kiff.requests.v1";

    /// <summary>A request message's hash, as both services write it: lowercase hex SHA-256.</summary>
    public static string Hash(byte[] message) => Convert.ToHexString(SHA256.HashData(message)).ToLowerInvariant();

    /// <summary>What the approval key signs: the scheme, the account, the one <c>signed_at</c>, and the two hashes in sorted order, a line each.</summary>
    public static byte[] Message(string tenant, string signedAt, string one, string other)
    {
        var hashes = string.CompareOrdinal(one, other) <= 0 ? new[] { one, other } : [other, one];
        return KiffSigning.Message([Scheme, tenant, signedAt, .. hashes]);
    }

    /// <summary>A request's signature header: <c>v3</c> for the one signature, <c>with</c> for the other request's hash.</summary>
    public static string Header(string signedAt, byte[] derSignature, string other) =>
        $"t={signedAt},v3={Convert.ToBase64String(derSignature)},with=sha256:{other}";

    /// <summary>
    /// The pair signed by this PC's passkey, the only key that signs it here (the iPad's is its approval key under Face ID;
    /// this PC's device key asks no one): one Windows Hello, its assertion's challenge base64url(sha256) of the message, in
    /// <c>KIFF-Passkey-Assertion: t=…,a=…,with=sha256:…</c>, each header with the other request's hash. That's KIFF's header
    /// for two requests under one passkey approval, as a <c>v3</c> device signature carries them (<c>with=sha256:</c> the
    /// other request message's hex), its challenge over <c>kiff.requests.v1</c>; KIFF's API and gateway both take it.
    /// </summary>
    public static async Task<(string Header, string Batch, string Draft)> SignAsync(Approvals.PasskeyApprover passkey, string tenant,
        StartRequest connect, StartRequest draft, string signedAt, CancellationToken cancel = default)
    {
        var (batch, cards) = Hashes(tenant, connect, draft, signedAt);
        var assertion = await passkey.AssertAsync(Message(tenant, signedAt, batch, cards), signedAt, cancel);
        return (Approvals.PasskeyApprover.HeaderName, $"{assertion},with=sha256:{cards}", $"{assertion},with=sha256:{batch}");
    }

    static (string Batch, string Cards) Hashes(string tenant, StartRequest connect, StartRequest draft, string signedAt) =>
        (Hash(KiffSigning.RequestV1(tenant, connect.Method, connect.Path, connect.Body, signedAt)),
         Hash(KiffSigning.RequestV1(tenant, draft.Method, draft.Path, draft.Body, signedAt)));
}

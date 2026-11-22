using System.Security.Cryptography;
using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Taf;

namespace Tafs.Core.Kiff;

/// <summary>One tool a grant asks for: its tool at the vendor, its name in KIFF, its title for people, and whether it only reads.</summary>
public sealed record GrantTool(string Tool, string? Name = null, string? Title = null, bool? ReadOnly = null, bool? SetupReads = null)
{
    /// <summary>One of the taf's own tools, not Setup's read.</summary>
    public bool Tafs => SetupReads != true;
    /// <summary>Its name in KIFF and at the relay: the Card's action.</summary>
    public string Action => Name ?? Tool;
}

/// <summary>What a grant asks for: the vendor's server, the taf's agent, its tools, a managed provider's connector, and its kind (<c>api_key</c>).</summary>
public sealed record GrantRequest(string Server, string Agent, IReadOnlyList<GrantTool> Tools, string? Provider = null, string? Toolkit = null,
    bool HasConnector = false, string? Kind = null);

/// <summary>A connection's health for people, as planque writes it: <c>state</c>, <c>ok</c> only while it works, and its words.</summary>
public sealed record GrantHealth(string State, bool Ok, string Words);

/// <summary>One thing the person can do with a connection now, as planque offers it: its id, words, how, and what it stops.</summary>
public sealed record GrantAction(string Id, string Words, string How, string? Stops);

/// <summary>
/// A grant as planque lists it (the iPad's <c>PlanqueClient.Grant</c>): what a taf asks to connect, its status, the words the
/// person reads before approving, and its digest. <see cref="DigestMatches"/> is set by the read, never read from planque: the
/// digest is the hash of what it covers as received, and only then may it be approved.
/// </summary>
/// <summary>A browser connection's Chrome: planque's id for it, its name as paired, and the sites the connection opens.</summary>
public sealed record ChromeConnection(string ChromeId, string? ChromeName, IReadOnlyList<string> Sites);

public sealed record PlanqueGrant(string Id, string Status, string Text, string Digest, GrantRequest Request, IReadOnlyList<(string At, string What)> Events)
{
    public string? StatusWords { get; init; }
    /// <summary>
    /// A browser connection on the owner's own Chrome (<c>request.browser</c>, <c>driver: extension</c>): the Chrome it runs in,
    /// that Chrome's name as paired, and its sites. None for any other connection (docs/chrome.md, "Your Chrome" 4).
    /// </summary>
    public ChromeConnection? Chrome { get; init; }
    public IReadOnlyList<string>? Terms { get; init; }
    public string? Account { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string>? UsedBy { get; init; }
    public GrantHealth? Health { get; init; }
    public IReadOnlyList<GrantAction>? Actions { get; init; }
    public IReadOnlyList<string>? Tools { get; init; }
    public string? Reason { get; init; }
    public bool? SignedIn { get; init; }
    public string? HeldUntil { get; init; }
    public string? Entry { get; init; }
    public string? Provider { get; init; }
    public string? AgentName { get; init; }
    public string? VendorName { get; init; }
    public bool DigestMatches { get; init; }
    /// <summary>The connection's <c>kiff.profile.v1</c> in the request, as planque sent it: its digest covers it.</summary>
    public JcsValue? Profile { get; init; }

    /// <summary>The vendor's name, as planque gives it, else its server.</summary>
    public string Vendor => VendorName ?? Request.Server;
    public bool Connected => Status == "connected";
    public bool Listed => Status == "listed";
    public bool NeedsReconnect => Status == "needs_reconnect";
    public bool TakesKey => Request.Kind == "api_key";
    public bool HoldsSignIn => Listed || (Status == "pending" && SignedIn == true);
}

/// <summary>
/// planque's grants, read as this device (the iPad's <c>PlanqueClient.grants</c>): <c>GET /v1/accounts/{tenant}/grants</c>
/// on planque, signed with <c>Planque-Device-Signature</c> and never with KIFF's token. Each grant's digest is checked here.
/// </summary>
public static class PlanqueGrants
{
    /// <summary>
    /// The account's grants, read as strictly as the iPad's decoder: one it can't read fails the whole read (none). Each
    /// one's digest is checked against what it covers (<see cref="DigestCovers"/>), from the answer's own bytes, numbers as
    /// written. Each grant is paired with its own entry in those bytes, by its place in the list, never looked up by its id:
    /// with a repeated id, one request's digest could vouch for another's. So a repeated id, or bytes that list another
    /// number of grants, fails the read.
    /// </summary>
    public static IReadOnlyList<PlanqueGrant>? Read(byte[] answer)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(answer);
            root = doc.RootElement.Clone();
        }
        catch (JsonException) { return null; }
        if (!Shape.Required(root, "grants", v => v.ValueKind == JsonValueKind.Array)) return null;
        var grants = new List<PlanqueGrant>();
        foreach (var g in root.GetProperty("grants").EnumerateArray())
        {
            if (Grant(g) is not { } grant) return null;
            grants.Add(grant);
        }
        if (grants.Select(g => g.Id).Distinct(StringComparer.Ordinal).Count() != grants.Count) return null;
        IReadOnlyList<JcsValue>? raws;
        try { raws = Jcs.Parse(answer)["grants"]?.AsArray; }
        // Bytes JCS can't read: every grant is read, and none matches.
        catch (Jcs.Failure) { return grants; }
        if (raws is null || raws.Count != grants.Count) return null;
        return grants.Select((grant, i) =>
        {
            var raw = raws[i];
            if (raw["id"]?.AsString != grant.Id || raw["request"] is not { } request) return grant;
            grant = grant with { Profile = request["profile"] };
            if (DigestCovers(raw) is not { } covered) return grant;
            try { return grant with { DigestMatches = Convert.ToHexString(SHA256.HashData(Jcs.Canonical(covered))).ToLowerInvariant() == grant.Digest }; }
            catch (Jcs.Failure) { return grant; }
        }).ToList();
    }

    /// <summary>
    /// What a grant's digest covers, as its <c>digest_v</c> says: with none, the request alone; with 2, the request with the
    /// words the person reads and their language, <c>{"lang", "request", "text"}</c>. None for a version this app doesn't know,
    /// or a v2 grant without its words or language: its digest then never matches, and nothing the person couldn't have read
    /// is approved.
    /// </summary>
    public static JcsValue? DigestCovers(JcsValue raw)
    {
        if (raw["request"] is not { } request) return null;
        return raw["digest_v"] switch
        {
            null => request,
            JcsValue.Number { Literal: "2" } when raw["lang"] is { AsString: { Length: > 0 } } lang && raw["text"] is { AsString: not null } text =>
                new JcsValue.Object(new Dictionary<string, JcsValue> { ["lang"] = lang, ["request"] = request, ["text"] = text }),
            _ => null,
        };
    }

    static PlanqueGrant? Grant(JsonElement g)
    {
        string[] required = ["id", "status", "text", "digest"];
        string[] optional = ["status_words", "account", "title", "reason", "held_until", "entry", "provider", "agent_name", "vendor"];
        if (!required.All(n => Shape.Required(g, n, Shape.Text)) || !optional.All(n => Shape.Optional(g, n, Shape.Text))
            || !Shape.Optional(g, "signed_in", Shape.Bool)
            || !new[] { "terms", "used_by", "tools" }.All(n => Shape.Optional(g, n, v => Shape.List(v, Shape.Text)))
            || !Shape.Optional(g, "actions", v => v.ValueKind == JsonValueKind.Array)
            || !Shape.Required(g, "events", v => Shape.List(v, e => Shape.Required(e, "at", Shape.Text) && Shape.Required(e, "what", Shape.Text)))
            || !Shape.Required(g, "request", IsRequest)) return null;
        string? T(JsonElement o, string n) => Lenient.Text(o, n);
        IReadOnlyList<string>? Words(string n) => Lenient.Field(g, n) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().Select(e => e.GetString()!).ToList() : null;
        var r = g.GetProperty("request");
        var connector = Lenient.Field(r, "connector") is { ValueKind: JsonValueKind.Object } c ? c : (JsonElement?)null;
        var request = new GrantRequest(T(r, "server")!, T(r, "agent")!,
            r.GetProperty("tools").EnumerateArray().Select(t => new GrantTool(T(t, "tool")!, T(t, "name"), T(t, "title"), Flag(t, "read_only"), Flag(t, "setup_reads"))).ToList(),
            connector is { } p ? T(p, "provider") : null, connector is { } k ? T(k, "toolkit") : null, connector is not null, T(r, "kind"));
        return new PlanqueGrant(T(g, "id")!, T(g, "status")!, T(g, "text")!, T(g, "digest")!, request,
            g.GetProperty("events").EnumerateArray().Select(e => (T(e, "at")!, T(e, "what")!)).ToList())
        {
            StatusWords = T(g, "status_words"),
            Chrome = Lenient.Field(r, "browser") is { ValueKind: JsonValueKind.Object } b && T(b, "driver") == "extension" && T(b, "chrome") is { Length: > 0 } chromeId
                ? new ChromeConnection(chromeId, T(b, "chrome_name"),
                    Lenient.Array(b, "sites").Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList())
                : null, Terms = Words("terms"), Account = T(g, "account"), Title = T(g, "title"), UsedBy = Words("used_by"),
            Health = Lenient.Field(g, "health") is { ValueKind: not JsonValueKind.Null } h ? Health(h) : null,
            Actions = Lenient.Field(g, "actions") is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().Select(Action).ToList() : null,
            Tools = Words("tools"), Reason = T(g, "reason"), SignedIn = Flag(g, "signed_in"), HeldUntil = T(g, "held_until"), Entry = T(g, "entry"),
            Provider = T(g, "provider"), AgentName = T(g, "agent_name"), VendorName = T(g, "vendor"),
        };
    }

    /// <summary>
    /// The request as the iPad's decoder takes it: its server, agent and tools (each with its tool) required; a connector, if
    /// any, an object of words.
    /// </summary>
    static bool IsRequest(JsonElement r) =>
        Shape.Required(r, "server", Shape.Text) && Shape.Required(r, "agent", Shape.Text) && Shape.Optional(r, "kind", Shape.Text)
        && Shape.Optional(r, "connector", c => c.ValueKind == JsonValueKind.Object && Shape.Optional(c, "provider", Shape.Text) && Shape.Optional(c, "toolkit", Shape.Text))
        && Shape.Required(r, "tools", v => Shape.List(v, t => Shape.Required(t, "tool", Shape.Text) && Shape.Optional(t, "name", Shape.Text)
            && Shape.Optional(t, "title", Shape.Text) && Shape.Optional(t, "read_only", Shape.Bool) && Shape.Optional(t, "setup_reads", Shape.Bool)));

    static bool? Flag(JsonElement o, string n) => Lenient.Field(o, n) is { ValueKind: JsonValueKind.True or JsonValueKind.False } v ? v.GetBoolean() : null;

    /// <summary>Health, read leniently, as the iPad's: anything it can't read is empty, never a failed read.</summary>
    static GrantHealth Health(JsonElement h) =>
        new(Lenient.Text(h, "state") ?? "", Lenient.Bool(h, "ok"), (Lenient.Text(h, "words") ?? "").Trim());

    /// <summary>An action, read leniently, as the iPad's: each of its words trimmed, none when it isn't words.</summary>
    static GrantAction Action(JsonElement a)
    {
        string W(string n) => (Lenient.Text(a, n) ?? "").Trim();
        return new(W("id"), W("words"), W("how"), W("stops") is { Length: > 0 } s ? s : null);
    }
}

using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Kiff;

/// <summary>
/// A Chrome paired to the account: planque's id, the name it was given, and what it says it is; with the connections that run
/// in it, as planque decides them (none when planque doesn't say: an older planque).
/// </summary>
public sealed record PairedChrome(string Id, string Name, string? Browser, string? Os, IReadOnlyList<ChromeUse>? Connections = null);

/// <summary>A connection that runs in a Chrome, as planque lists it: its grant, the taf's name, and the sites it reaches.</summary>
public sealed record ChromeUse(string Grant, string Name, IReadOnlyList<string> Sites);

/// <summary>
/// Account › Chrome (docs/chrome.md), as the iPad's <c>ChromesView</c> and <c>PlanqueClient</c>'s Chrome routes: the Chromes
/// paired to the account, read and unpaired as this device (planque's <c>signedSafe</c> routes: the device key is enough, with
/// no approval window). Pairing binds the approval key the extension later trusts, so it stays on the phone (agentgabo's
/// read of planque, kiff/work#20, 20:00). planque's own switch decides whether Chrome shows at all.
/// </summary>
public static class PlanqueChromes
{
    public static string ListPath(string tenant) => $"/v1/accounts/{CloudRead.PathPart(tenant)}/chromes";
    public static string ChromePath(string tenant, string chrome) => ListPath(tenant) + "/" + CloudRead.PathPart(chrome);

    /// <summary>The Chromes, as planque lists them; one it can't read is left out. None for an answer that isn't a list.</summary>
    public static IReadOnlyList<PairedChrome>? Read(JsonElement a) =>
        a.ValueKind == JsonValueKind.Object && Lenient.Field(a, "chromes") is { ValueKind: JsonValueKind.Array } list
            ? list.EnumerateArray()
                .Where(c => c.ValueKind == JsonValueKind.Object && Lenient.Text(c, "chrome_id") is { Length: > 0 } && Lenient.Text(c, "name") is not null)
                .Select(c => new PairedChrome(Lenient.Text(c, "chrome_id")!, Lenient.Text(c, "name")!, Lenient.Text(c, "browser"), Lenient.Text(c, "os"),
                    Lenient.Field(c, "connections") is { ValueKind: JsonValueKind.Array } on ? Uses(on) : null))
                .ToList()
            : null;

    /// <summary>
    /// Whether Account shows Chrome, from planque's answer to the list: only once it lists them. Its route off (404), or a person
    /// who may not connect tools (403, <c>capConnect</c>), shows none, as the iPad does while <c>chromes</c> is nil.
    /// </summary>
    public static bool Offered(int status) => status is >= 200 and <= 299;

    /// <summary>"Chrome on Windows", when the Chrome says both.</summary>
    public static string? Says(WordBook w, PairedChrome c) =>
        c.Browser is { Length: > 0 } b && c.Os is { Length: > 0 } o ? w.Get("chrome.browser_on", ("browser", b), ("system", o)) : null;

    /// <summary>
    /// planque's connections on a Chrome (<c>connections: [{grant, name, sites}]</c>, planque#370): the live ones, decided by
    /// planque once so no app writes the rule again. One without a grant is left out; its sites are the texts among them.
    /// </summary>
    static List<ChromeUse> Uses(JsonElement on) =>
        on.EnumerateArray()
            .Where(u => u.ValueKind == JsonValueKind.Object && Lenient.Text(u, "grant") is { Length: > 0 })
            .Select(u => new ChromeUse(Lenient.Text(u, "grant")!, Lenient.Text(u, "name") ?? "",
                Lenient.Array(u, "sites").Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => x.Length > 0).ToList()))
            .ToList();

    /// <summary>
    /// The sites a Chrome's connections reach, as the owner reads them ("wikipedia.org, shop.example"): each once, in planque's
    /// order. Empty when it has none, or planque didn't say.
    /// </summary>
    public static string Sites(PairedChrome c)
    {
        var seen = new List<string>();
        foreach (var site in (c.Connections ?? []).SelectMany(u => u.Sites)) if (!seen.Contains(site)) seen.Add(site);
        return string.Join(", ", seen);
    }

    /// <summary>"Used for wikipedia.org, shop.example" under a Chrome; none when nothing runs in it.</summary>
    public static string? UsedFor(WordBook w, PairedChrome c) => Sites(c) is { Length: > 0 } sites ? w.Get("chrome.used_for", ("sites", sites)) : null;

    /// <summary>What Unpair says it ends, before it asks ("Your Chrome" 3); none when nothing runs in it.</summary>
    public static string? UnpairEnds(WordBook w, PairedChrome c) => Sites(c) is { Length: > 0 } sites ? w.Get("chrome.unpair_ends", ("sites", sites)) : null;
}

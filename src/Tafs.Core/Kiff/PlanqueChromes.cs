using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Kiff;

/// <summary>A Chrome paired to the account: planque's id, the name it was given, and what it says it is.</summary>
public sealed record PairedChrome(string Id, string Name, string? Browser, string? Os);

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
                .Select(c => new PairedChrome(Lenient.Text(c, "chrome_id")!, Lenient.Text(c, "name")!, Lenient.Text(c, "browser"), Lenient.Text(c, "os")))
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
    /// The connections on a Chrome (docs/chrome.md, "Your Chrome" 3 and 4): the account's live browser connections that run in it,
    /// each as "Wiki lookup · wikipedia.org" (the connection's name, then its sites). Unpairing the Chrome ends exactly these.
    /// </summary>
    public static IReadOnlyList<string> ConnectionsOn(IEnumerable<PlanqueGrant> grants, string chromeId) =>
        grants.Where(g => g.Chrome?.ChromeId == chromeId && g.Status != "disconnected")
            .Select(g => string.Join(" · ", new[] { g.Title ?? g.AgentName ?? g.Request.Agent, string.Join(", ", g.Chrome!.Sites) }.Where(x => x.Length > 0)))
            .ToList();
}

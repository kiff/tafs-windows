using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;

namespace Tafs.Core.Connections;

/// <summary>One plain line of a key's sheet: its kind picks only its mark; the words are tafs cloud's.</summary>
public sealed record SheetLine(string Kind, string Text);

/// <summary>A link in the sheet's legal line: its words and where it goes (https only, else drawn as words).</summary>
public sealed record SheetLink(string Words, string Url);

/// <summary>
/// The words of a key's sheet, by its grant (the iPad's <c>TafsCloud.ConnectionSheet</c>, docs/secure-connection.md): tafs cloud
/// works them out from the grant and its catalog entry, in the screen's language, and this PC draws them, filling in only the
/// key's last 4 characters (<c>{last4}</c>) and the vendor (<c>{vendor}</c>).
/// </summary>
public sealed record KeySheet(string Vendor, string? Host, string Title, IReadOnlyList<SheetLine> Lines, string PasteButton, string PasteWhat,
    string Pasted, string Cleared, string? Next, string? DetailsTitle, IReadOnlyList<(string Label, string Value)> Details, string? CardTitle,
    string? Legal, SheetLink? Terms, SheetLink? Privacy, string Button, string? Receipt, string? Disconnect)
{
    /// <summary><c>GET /v1/connect/grants/{grant}/sheet</c>, with this PC's grants read proof beside it.</summary>
    public static string Path(string grant) => "/v1/connect/grants/" + CloudRead.PathPart(grant) + "/sheet";

    /// <summary>tafs cloud's answer, read as strictly as the iPad's decoder: its vendor, title, paste and button are required.</summary>
    public static KeySheet? Read(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || Lenient.Field(a, "vendor") is not { ValueKind: JsonValueKind.Object } vendor
            || Lenient.Text(vendor, "name") is not { } name || Lenient.Text(a, "title") is not { } title || Lenient.Text(a, "button") is not { } button
            || Lenient.Field(a, "paste") is not { ValueKind: JsonValueKind.Object } paste) return null;
        string?[] pasteParts = [Lenient.Text(paste, "button"), Lenient.Text(paste, "what"), Lenient.Text(paste, "pasted"), Lenient.Text(paste, "cleared")];
        if (pasteParts.Any(p => p is null)) return null;
        var lines = Lenient.Array(a, "lines").Where(l => Lenient.Text(l, "kind") is not null && Lenient.Text(l, "text") is not null)
            .Select(l => new SheetLine(Lenient.Text(l, "kind")!, Lenient.Text(l, "text")!)).ToList();
        var details = Lenient.Field(a, "details") is { ValueKind: JsonValueKind.Object } d ? d : (JsonElement?)null;
        var detailLines = details is { } dl ? Lenient.Array(dl, "lines").Where(l => Lenient.Text(l, "label") is not null && Lenient.Text(l, "value") is not null)
            .Select(l => (Lenient.Text(l, "label")!, Lenient.Text(l, "value")!)).ToList() : [];
        var legal = Lenient.Field(a, "legal") is { ValueKind: JsonValueKind.Object } lg ? lg : (JsonElement?)null;
        SheetLink? Link(string n) => legal is { } l && Lenient.Field(l, n) is { ValueKind: JsonValueKind.Object } k && Lenient.Text(k, "words") is { } words
            && Lenient.Text(k, "url") is { } url ? new(words, url) : null;
        var receipt = Lenient.Field(a, "receipt") is { ValueKind: JsonValueKind.Object } r ? r : (JsonElement?)null;
        return new(name, Lenient.Text(vendor, "host"), title, lines, pasteParts[0]!, pasteParts[1]!, pasteParts[2]!, pasteParts[3]!, Lenient.Text(paste, "next"),
            details is { } dt ? Lenient.Text(dt, "title") : null, detailLines, Lenient.Text(a, "card_title"),
            legal is { } lt ? Lenient.Text(lt, "text") : null, Link("terms"), Link("privacy"), button,
            receipt is { } rc ? Lenient.Text(rc, "text") : null, receipt is { } rd ? Lenient.Text(rd, "disconnect") : null);
    }

    /// <summary>
    /// Whether the sheet's "reads only" line is true of the grant this PC read to sign its digest: only with at least one tool,
    /// every one a read. The line people trust most is never tafs cloud's word alone.
    /// </summary>
    public bool SaysTrue(PlanqueGrant grant) =>
        !Lines.Any(l => l.Kind == "reads_only") || (grant.Request.Tools.Count > 0 && grant.Request.Tools.All(t => t.ReadOnly == true));

    /// <summary>A line of tafs cloud's with this PC's part filled in: the key's last 4, and the vendor where tafs cloud left it.</summary>
    public string Filled(string text, string last4) => text.Replace("{last4}", last4).Replace("{vendor}", Vendor);

    /// <summary>The legal line as words, its <c>{terms}</c> and <c>{privacy}</c> replaced by their links' words.</summary>
    public string? LegalWords => Legal?.Replace("{terms}", Terms?.Words ?? "").Replace("{privacy}", Privacy?.Words ?? "");

    /// <summary>The key from a paste: the text without the blanks or line ends a copy adds. Nothing pasted, nothing taken.</summary>
    public static string? Taken(string? pasted) => pasted?.Trim() is { Length: > 0 } key ? key : null;

    /// <summary>Its last 4 characters, the only part ever drawn; a key too short for 4 to hide the rest shows none.</summary>
    public static string Last4(string key) => key.Length >= 12 ? key[^4..] : "";

    /// <summary>planque's key route: <c>POST /v1/accounts/{tenant}/grants/{id}/key</c>, the key itself only in this header.</summary>
    public static string KeyPath(string tenant, string grant) => PlanqueProof.GrantsPath(tenant) + "/" + CloudRead.PathPart(grant) + "/key";

    public const string KeyHeader = "Planque-Vendor-Key";

    /// <summary>
    /// The signed body that gives a grant its key, as the iPad's <c>KeyBody</c>: the grant's digest, the key's SHA-256, and with a
    /// profile both its digests. The signature covers which key; the key itself is never in what's signed or logged.
    /// </summary>
    public static byte[] KeyBody(string digest, string key, string? profileSha256 = null, string? wordsSha256 = null)
    {
        var o = new JsonObject { ["digest"] = digest, ["key_sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim()))).ToLowerInvariant() };
        if (profileSha256 is not null && wordsSha256 is not null)
            o["kiff_profile"] = new JsonObject { ["profile_sha256"] = profileSha256, ["words_sha256"] = wordsSha256 };
        return Encoding.UTF8.GetBytes(o.ToJsonString());
    }
}

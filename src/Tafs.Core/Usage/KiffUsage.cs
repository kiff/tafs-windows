using System.Globalization;
using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Usage;

/// <summary>A pack of credits KIFF sells: its id, name and KIFF's line, and whether it's the First month pack (bought once).</summary>
public sealed record UsagePack(string Id, string Name, string Line, bool Once);

/// <summary>
/// A member's request to the account's admins (<c>kind</c> <c>raise</c>, or a <c>pack</c> naming it): its reason, and where it
/// stands (open, approved, declined, withdrawn or expired), with the admin's note when declined.
/// </summary>
public sealed record PackRequest(string Id, string Kind, string? Pack, string Reason, string Status, string? Note);

/// <summary>
/// KIFF's usage (<c>GET /v1/me/usage</c>), as the iPad's <c>KIFFClient.Usage</c>: the plan, who bills it, the packs on sale,
/// and members' requests (every open one for someone who manages billing, this person's own otherwise). Nothing that moves
/// money happens on Windows in this release: Raise and buying say "Approve on your phone" where their buttons would be.
/// </summary>
public sealed record KiffUsage(string? Plan, string? BillingSource, DateTimeOffset? AppleSoloUntil, IReadOnlyList<UsagePack> Packs,
    IReadOnlyList<PackRequest> Requests)
{
    public const string Path = "/v1/me/usage";
    public const string RequestsPath = "/v1/me/pack-requests";
    public static string WithdrawPath(string id) => RequestsPath + "/" + CloudRead.PathPart(id) + "/withdraw";
    public static string DeclinePath(string id) => RequestsPath + "/" + CloudRead.PathPart(id) + "/decline";

    /// <summary>KIFF's answer; a field it can't read is none, a pack or request it can't read is left out.</summary>
    public static KiffUsage? Read(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) return null;
        var billing = Lenient.Field(a, "billing") is { ValueKind: JsonValueKind.Object } b ? b : (JsonElement?)null;
        var packs = Lenient.Field(a, "store") is { ValueKind: JsonValueKind.Object } store
            ? Lenient.Array(store, "packs").Where(p => p.ValueKind == JsonValueKind.Object && Lenient.Text(p, "id") is not null && Lenient.Text(p, "name") is not null)
                .Select(p => new UsagePack(Lenient.Text(p, "id")!, Lenient.Text(p, "name")!, Lenient.Text(p, "line") ?? "", Lenient.Bool(p, "once"))).ToList()
            : [];
        var requests = Lenient.Array(a, "requests").Where(r => r.ValueKind == JsonValueKind.Object && Lenient.Text(r, "id") is not null && Lenient.Text(r, "status") is not null)
            .Select(r => new PackRequest(Lenient.Text(r, "id")!, Lenient.Text(r, "kind") ?? "pack", Lenient.Text(r, "pack"), Lenient.Text(r, "reason") ?? "",
                Lenient.Text(r, "status")!, Lenient.Text(r, "note"))).ToList();
        return new(Lenient.Text(a, "plan"), billing is { } bill ? Lenient.Text(bill, "source") : null,
            billing is { } bill2 ? Lenient.At(bill2, "apple_solo_until") : null, packs, requests);
    }

    /// <summary>Who bills the plan, in one line; none when no plan is billed.</summary>
    public string? BillingLine(WordBook w) => BillingSource switch
    {
        "apple" => w.Get("billing.by_apple"),
        "google" => w.Get("billing.by_google_play"),
        "stripe" => w.Get("billing.by_kiff"),
        "contract" => w.Get("billing.by_agreement"),
        _ => null,
    };

    /// <summary>
    /// What someone who doesn't manage billing asks an admin for on this plan, as the iPad: a raise of the spend limit on
    /// Company (<c>team</c>), a pack on Solo while packs are on sale; nothing on others. True is a raise.
    /// </summary>
    public bool? Asks => Plan switch
    {
        "team" => true,
        "solo" => Packs.Any(p => !p.Once) ? false : null,
        _ => null,
    };

    /// <summary>
    /// Where to buy, for someone who manages billing: a PC has no store of its own, so "Buy Solo or credits in the tafs app on
    /// your phone." (<c>billing.buy_on_phone</c>), and, unless the plan is Team already, a link to get Team on kiff.dev
    /// (<c>billing.team_on_web</c>, <see cref="TeamCheckout"/>). Neither under a company's agreement, which nobody buys into
    /// here; neither for a member, who asks an admin instead.
    /// </summary>
    public (bool OnPhone, bool TeamOnWeb) WhereToBuy(bool managesBilling) =>
        !managesBilling || BillingSource == "contract" ? (false, false) : (true, Plan != "team");

    /// <summary>Where Team is bought on the web: kiff.dev's checkout, which signs the person in first.</summary>
    public static readonly Uri TeamCheckout = new("https://kiff.dev/dashboard/checkout");

    /// <summary>The packs a member may ask for: those that can be bought again.</summary>
    public IReadOnlyList<UsagePack> Askable => Packs.Where(p => !p.Once).ToList();

    /// <summary>This member's own request still open.</summary>
    public PackRequest? Waiting(bool managesBilling) => managesBilling ? null : Requests.FirstOrDefault(r => r.Status == "open");

    /// <summary>The member's request while it waits, in the line both apps share.</summary>
    public static string AskedLine(WordBook w, PackRequest r) => w.Get(r.Kind == "raise" ? "billing.ask.asked_raise" : "billing.ask.asked_pack");

    /// <summary>The member's latest answered request, in the lines both apps share; none while none was answered.</summary>
    public string? LastAnswer(WordBook w, bool managesBilling)
    {
        if (managesBilling || Requests.FirstOrDefault() is not { } r) return null;
        return r.Status switch
        {
            "approved" => w.Get(r.Kind == "raise" ? "billing.ask.raised" : "billing.ask.added_pack"),
            "declined" => r.Note is { Length: > 0 } note ? w.Get("billing.ask.declined_note", ("note", note)) : w.Get("billing.ask.declined"),
            "expired" => w.Get("billing.ask.expired"),
            _ => null,
        };
    }

    /// <summary>Open requests, for someone who manages billing.</summary>
    public IReadOnlyList<PackRequest> Open(bool managesBilling) => managesBilling ? Requests.Where(r => r.Status == "open").ToList() : [];

    /// <summary>An open request as someone who manages billing reads it: "Someone asks to raise the spend limit.", "Someone asks for Rayo."</summary>
    public string RequestLine(WordBook w, PackRequest r) =>
        r.Kind == "raise" ? w.Get("billing.ask.someone_raise") : w.Get("billing.ask.someone_pack", ("pack", PackLine(w, r.Pack)));

    /// <summary>A pack's line, as KIFF sells it; "a pack" when it isn't on sale.</summary>
    public string PackLine(WordBook w, string? id) =>
        id is null ? w.Get("usage.pack_credits") : Packs.FirstOrDefault(p => p.Id == id)?.Line is { Length: > 0 } line ? line
        : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id);

    /// <summary>The ask: <c>{"kind": "raise", "reason"}</c>, or <c>{"pack", "reason"}</c> (no kind means a pack).</summary>
    public static byte[] AskBody(bool raise, string? pack, string reason)
    {
        var body = new Dictionary<string, string> { ["reason"] = reason.Trim() };
        if (raise) body["kind"] = "raise";
        else if (pack is not null) body["pack"] = pack;
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    public static byte[] DeclineBody(string note) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["note"] = note.Trim() });

    /// <summary>
    /// Declines a member's request, with a note, as the iPad's signed call: Windows Hello first, then the device key's
    /// signature over exactly the request (<c>KIFF-Device-Signature</c>), then the POST. Closed, failing or refused, why;
    /// nothing is sent until it's confirmed and signed. A sign-out meanwhile sends nothing more.
    /// </summary>
    public static async Task<string?> DeclineAsync(WordBook w, string tenant, PackRequest r, string note, Func<string, Task<StepConfirmed>> confirm,
        Func<byte[], Task<byte[]>> signDer, Func<string, byte[], string, Task<(int Status, string Body)>> post, Func<string> signedAt, Func<bool> current)
    {
        var confirmed = await confirm(w.Get("usage.requests.decline"));
        if (!current()) return null;
        if (confirmed != StepConfirmed.Yes) return confirmed == StepConfirmed.Failed ? w.Get("common.not_sent_unconfirmed") : null;
        var path = DeclinePath(r.Id);
        var body = DeclineBody(note);
        var at = signedAt();
        string header;
        try { header = KiffSigning.SignatureHeaderV1(at, await signDer(KiffSigning.RequestV1(tenant, "POST", path, body, at))); }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or System.Runtime.InteropServices.COMException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            return w.Get("common.not_sent_unconfirmed");
        }
        if (!current()) return null;
        int status;
        try { (status, _) = await post(path, body, header); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { status = 0; }
        if (!current()) return null;
        return status is >= 200 and < 300 ? null : KiffErrors.Words(w, status);
    }
}

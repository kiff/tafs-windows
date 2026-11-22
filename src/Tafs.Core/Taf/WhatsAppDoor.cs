using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tafs.Core.Kiff;

namespace Tafs.Core.Taf;

/// <summary>What WhatsApp's sign-up page hands back: the taf, this PC's one-use state, Meta's code and the number's ids.</summary>
public sealed record WhatsAppSignedUp(string Taf, string State, string Code, string PhoneNumberId, string WabaId);

/// <summary>
/// Adding a WhatsApp door (tafs docs/doors.md, the iPad's <c>DoorsModel.add</c> for WhatsApp): tafs cloud's page runs Meta's
/// embedded sign-up in the browser, which hands back to the app on <c>tafs://doors/whatsapp</c>, and the app makes the door with
/// the code, as the session. The iPad's return is bound to the sign-up it started (<c>ASWebAuthenticationSession</c>); on
/// Windows any link can open <c>tafs:</c>, so this PC sends a one-use <c>state</c>, which the page echoes, and takes only the
/// return carrying it, for that taf, within 15 minutes (the PM's (a), kiff/work#20, 23 Nov 23:59).
/// </summary>
public static class WhatsAppDoor
{
    /// <summary>How long a sign-up waits for its return.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(15);

    /// <summary>A one-use state: 32 random bytes, base64url (43 characters of <c>[A-Za-z0-9_-]</c>).</summary>
    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>tafs cloud's sign-up page for this taf, with this attempt's state.</summary>
    public static Uri SignupPage(Uri api, string taf, string state) =>
        new(api, "/doors/whatsapp/connect?taf=" + Uri.EscapeDataString(taf) + "&state=" + Uri.EscapeDataString(state));

    /// <summary>
    /// The page's return, <c>tafs://doors/whatsapp?taf=…&amp;state=…&amp;code=…&amp;phone_number_id=…&amp;waba_id=…</c>: none for any
    /// other URL, or one without its taf, state or code. The ids may be empty, as the page sends them when Meta gave none.
    /// </summary>
    public static WhatsAppSignedUp? Read(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme != PlanqueSignIn.Scheme || url.Host != "doors" || url.AbsolutePath != "/whatsapp") return null;
        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var name = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            // A name given twice is no return: which one the page meant can't be told.
            if (!items.TryAdd(name, pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "")) return null;
        }
        string? Value(string name) => items.GetValueOrDefault(name) is { Length: > 0 } v ? v : null;
        return Value("taf") is { } taf && Value("state") is { } state && Value("code") is { } code
            ? new(taf, state, code, items.GetValueOrDefault("phone_number_id") ?? "", items.GetValueOrDefault("waba_id") ?? "")
            : null;
    }

    /// <summary>
    /// Whether a return is this attempt's: the same taf, the same state (compared in fixed time), and within <see cref="Wait"/>
    /// of when the page was opened.
    /// </summary>
    public static bool Takes(WhatsAppSignedUp back, string taf, string state, DateTimeOffset opened, DateTimeOffset now) =>
        back.Taf == taf && now >= opened && now - opened <= Wait
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(back.State), Encoding.UTF8.GetBytes(state));

    /// <summary>The door tafs cloud makes (<c>POST /v1/tafs/{id}/doors</c>): its app, Meta's code and the number's ids.</summary>
    public static byte[] DoorBody(string app, WhatsAppSignedUp back) =>
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
        {
            ["app"] = app, ["code"] = back.Code, ["phone_number_id"] = back.PhoneNumberId, ["waba_id"] = back.WabaId,
        });

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

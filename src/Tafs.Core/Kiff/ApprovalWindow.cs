using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Taf;

namespace Tafs.Core.Kiff;

/// <summary>
/// An approval window as KIFF keeps it (KIFF's desktop client contract, §3): its id, when it ends, whether it's open, the key
/// that signs inside it (<c>device</c>), and KIFF's line for the next tap.
/// </summary>
public sealed record OpenWindow(string Id, DateTimeOffset? Expires, bool Open, string? Key, string? Words);

/// <summary>What planque's approve answered: its status, the vendor's sign-in page, or the code that confirms a sign-in it holds.</summary>
public sealed record PlanqueApproved(string Status, Uri? SignIn, string? ConfirmCode);

/// <summary>
/// The approval window a computer's own passkey opens (KIFF's desktop client contract §2.6 and §3): one
/// Windows Hello opens it (<c>POST /v1/me/window</c>, approved by this PC's passkey's assertion over exactly that request);
/// inside it, for a few minutes, this session's TPM key signs planque's <c>approve</c> and <c>confirm</c> as taps
/// (<c>planque.request.v2</c>, with the window's id), with no prompt. KIFF's own approval routes never take a window's tap: a
/// Card's confirm still asks Windows Hello each time.
/// </summary>
public static class ApprovalWindow
{
    /// <summary><c>POST</c> opens one; <c>GET</c> reads the open one.</summary>
    public const string Path = "/v1/me/window";

    /// <summary><c>POST</c>, device-signed: closed, when the screen locks or on sign-out.</summary>
    public static string ClosePath(string id) => Path + "/" + CloudRead.PathPart(id) + "/close";

    /// <summary>
    /// The opening's body: <c>{"message": "kiff.window.v1", "expires", "nonce"}</c>, ending at most 5 minutes from now (KIFF caps
    /// it there), with a fresh nonce of 32 base64url characters. Nothing more: unknown fields are refused.
    /// </summary>
    public static byte[] OpenBody(DateTimeOffset now, string? nonce = null) =>
        Encoding.UTF8.GetBytes(new JsonObject
        {
            ["message"] = "kiff.window.v1",
            ["expires"] = KiffSigning.Time(now.AddMinutes(5)),
            ["nonce"] = nonce ?? KiffSigning.Base64Url(RandomNumberGenerator.GetBytes(24)),
        }.ToJsonString());

    /// <summary>KIFF's <c>{"window": {…}}</c>: the window, or none (<c>null</c>, ended, or one it can't read).</summary>
    public static OpenWindow? Read(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || Lenient.Field(a, "window") is not { ValueKind: JsonValueKind.Object } w) return null;
        if (Lenient.Text(w, "id") is not { } id || !IsId(id)) return null;
        return new(id, Lenient.At(w, "expires"), Lenient.Bool(w, "open"), Lenient.Text(w, "key"), Lenient.Text(w, "words"));
    }

    /// <summary>A window id: <c>win_</c> and 26 characters from <c>a-z2-7</c>.</summary>
    public static bool IsId(string id) =>
        id.Length == 30 && id.StartsWith("win_", StringComparison.Ordinal) && id[4..].All(c => c is >= 'a' and <= 'z' or >= '2' and <= '7');

    /// <summary>Whether a window read can be signed in now: open, a device key's, and not past its end (with 10 s to spare).</summary>
    public static bool Usable(OpenWindow? w, DateTimeOffset now) =>
        w is { Open: true, Key: "device" } && (w.Expires is not { } end || end > now.AddSeconds(10));

    /// <summary>KIFF's code when a tap's window has ended: open another, then the same call again.</summary>
    public const string Ended = "window_required";

    /// <summary>planque's <c>device.MessageWindow</c>: <see cref="PlanqueProof.Message"/>'s lines under <c>planque.request.v2</c>, the window's id last.</summary>
    public static byte[] PlanqueMessage(Uri api, string tenant, string method, string uri, byte[] body, string signedAt, string window) =>
        KiffSigning.Message("planque.request.v2", api.AbsoluteUri.TrimEnd('/'), tenant, method, uri, KiffSigning.Sha256Hex(body), signedAt, window);

    /// <summary>
    /// <c>Planque-Device-Signature</c> for a tap inside the window, as the iPad's <c>PlanqueClient.send</c> with its <c>window</c>:
    /// <c>t=…,v2=…,w=…,d=…</c>, the device key's signature over <see cref="PlanqueMessage"/> for exactly this request.
    /// </summary>
    public static async Task<string> PlanqueTapAsync(string tenant, string device, string window, IKiffSigner key, string signedAt,
        string method, string uri, byte[] body, CancellationToken cancel = default)
    {
        var signature = await key.SignDerAsync(PlanqueMessage(PlanqueProof.Api, tenant, method, uri, body, signedAt, window), cancel);
        return $"t={signedAt},v2={Convert.ToBase64String(signature)},w={window},d={device}";
    }

    /// <summary>planque's approve of a grant: <c>POST /v1/accounts/{tenant}/grants/{id}/approve</c>.</summary>
    public static string ApprovePath(string tenant, string grant) => PlanqueProof.GrantsPath(tenant) + "/" + CloudRead.PathPart(grant) + "/approve";

    /// <summary>planque's confirm of a vendor sign-in with the code it handed back: <c>…/grants/{id}/confirm</c>.</summary>
    public static string ConfirmPath(string tenant, string grant) => PlanqueProof.GrantsPath(tenant) + "/" + CloudRead.PathPart(grant) + "/confirm";

    /// <summary>
    /// The approve body, as the iPad's <c>PlanqueClient.ApproveBody</c>: the grant's digest as shown, and, for a grant with a
    /// profile, the digests of that profile and of KIFF's words for it. Signed, so the signature covers both.
    /// </summary>
    public static byte[] ApproveBody(string digest, string? profileSha256 = null, string? wordsSha256 = null)
    {
        var o = new JsonObject { ["digest"] = digest };
        if (profileSha256 is not null && wordsSha256 is not null)
            o["kiff_profile"] = new JsonObject { ["profile_sha256"] = profileSha256, ["words_sha256"] = wordsSha256 };
        return Encoding.UTF8.GetBytes(o.ToJsonString());
    }

    /// <summary>The confirm body: <c>{"confirm": "&lt;code&gt;"}</c>.</summary>
    public static byte[] ConfirmBody(string code) => Encoding.UTF8.GetBytes(new JsonObject { ["confirm"] = code }.ToJsonString());

    /// <summary>
    /// planque's answer to an approve, as the iPad's <c>PlanqueClient.Approved</c>: its status (required), the vendor's sign-in
    /// page to open (https only), and, with <c>signed_in</c> and no page, the one-use code that confirms the sign-in planque
    /// already holds. None for an answer that isn't one.
    /// </summary>
    public static PlanqueApproved? Approved(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || Lenient.Text(a, "status") is not { } status) return null;
        var signIn = Lenient.Text(a, "sign_in_url") is { } s && Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u : null;
        return new(status, signIn, Lenient.Text(a, "confirm"));
    }
}

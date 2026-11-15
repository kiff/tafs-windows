using System.Globalization;
using System.Text.Json;
using Tafs.Core.Taf;

namespace Tafs.Core.Kiff;

/// <summary>
/// The time a signed request carries, as the iPad's <c>SigningTime</c>: RFC 3339 in UTC with milliseconds, never the same
/// twice in this app, so two reads in the same second aren't taken for a replay by planque or KIFF.
/// </summary>
public static class SigningTime
{
    static readonly object Lock = new();
    static long last = long.MinValue;

    public static string Next(DateTimeOffset now)
    {
        lock (Lock)
        {
            var ms = Math.Max(now.ToUnixTimeMilliseconds(), last == long.MinValue ? long.MinValue : last + 1);
            last = ms;
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }
    }
}

/// <summary>
/// planque's signed read of this account's grants, which tafs cloud forwards with a taf's Setup so its connected apps can
/// be read (<c>Planque-Device-Signature</c>), as the iPad's <c>PlanqueClient.grantsReadProof</c>. It's signed with the
/// session's TPM key, which asks no one: the iPad's current key, which signs Setup's and planque's requests. It grants
/// nothing; it lets planque read the grants as this device.
/// </summary>
public static class PlanqueProof
{
    /// <summary>planque, as the iPad's <c>PlanqueClient.baseURL</c>.</summary>
    public static readonly Uri Api = new("https://planque.kiff.dev");

    /// <summary>What a device signs for planque (planque's <c>device.Message</c>): seven lines, no trailing newline.</summary>
    public static byte[] Message(Uri api, string tenant, string method, string uri, byte[] body, string signedAt) =>
        KiffSigning.Message("planque.request.v1", api.AbsoluteUri.TrimEnd('/'), tenant, method, uri, KiffSigning.Sha256Hex(body), signedAt);

    public static string GrantsPath(string tenant) => $"/v1/accounts/{CloudRead.PathPart(tenant)}/grants";

    /// <summary>
    /// Whether a planque call may go to <paramref name="url"/>: https, on exactly planque's host, on the default port, with no
    /// user in it. It carries this device's signature only: KIFF's token never goes to planque (<see cref="KeptSessions.MaySendTo"/>
    /// doesn't list it).
    /// </summary>
    public static bool IsPlanque(Uri url) =>
        url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps && url.UserInfo.Length == 0 && url.IsDefaultPort
        && string.Equals(url.Host, Api.Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>t=…,v1=…,d=…</c>: the time, the signature over planque's <c>GET</c> of the account's grants, and this device's id
    /// in KIFF, loaded when it isn't known. None with no account or no device, as the iPad's <c>notSigning</c> and
    /// <c>noDevice</c>; a signature that fails is the caller's to treat as none, as the iPad's <c>try?</c>.
    /// </summary>
    public static async Task<string?> GrantsReadAsync(string? tenant, string? device, Func<Task<string?>> loadDevice, IKiffSigner key,
        Func<string> signedAt, CancellationToken cancel = default)
    {
        if (tenant is null) return null;
        device ??= await loadDevice();
        if (device is null) return null;
        var at = signedAt();
        var signature = await key.SignDerAsync(Message(Api, tenant, "GET", GrantsPath(tenant), [], at), cancel);
        return $"t={at},v1={Convert.ToBase64String(signature)},d={device}";
    }

    /// <summary>KIFF's <c>GET /v1/me/devices</c>: this device's id, as the iPad's <c>thisDeviceID</c>; none when it isn't listed yet.</summary>
    public const string DevicesPath = "/v1/me/devices";

    public static string? ThisDevice(JsonElement answer) =>
        answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("devices", out var devices) && devices.ValueKind == JsonValueKind.Array
            ? devices.EnumerateArray()
                .Where(d => d.ValueKind == JsonValueKind.Object && d.TryGetProperty("this_device", out var t) && t.ValueKind == JsonValueKind.True)
                .Select(d => d.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null)
                .FirstOrDefault(id => id is not null && id.StartsWith("dev_", StringComparison.Ordinal))
            : null;
}

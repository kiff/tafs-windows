using System.Text.Json;

namespace Tafs.Core.Kiff;

/// <summary>How KIFF lets this session approve (the token's approval_mode). Anything else counts as View.</summary>
public enum ApprovalMode { View, Key, Passkey }

/// <summary>
/// This PC's session, kept across launches: for KIFF's exact API host, with the TPM key it's bound to.
/// Its token is never printed.
/// </summary>
public sealed record KeptSession(
    string Host, string AccessToken, string Tenant, string Subject, string? DeviceId, ApprovalMode Mode, DateTimeOffset Expires,
    string KeyName)
{
    public override string ToString() => $"KeptSession {{ Host = {Host}, Tenant = {Tenant}, Mode = {Mode}, AccessToken = (hidden) }}";

    /// <summary>
    /// This PC's own Windows Hello passkey for kiff.dev, by its credential id (base64url), for a session paired with one:
    /// what Windows Hello is asked to assert with. An id, not a secret: the key never leaves the TPM. None otherwise.
    /// </summary>
    public string? PasskeyId { get; init; }

    /// <summary>A session KIFF just handed over, to keep: its expiry from expires_in, to the second.</summary>
    public static KeptSession Of(Uri api, string token, string tenant, string subject, string? deviceId, ApprovalMode mode,
        TimeSpan expiresIn, DateTimeOffset now, string keyName) =>
        new(api.Host, token, tenant, subject, deviceId, mode, DateTimeOffset.FromUnixTimeSeconds((now + expiresIn).ToUnixTimeSeconds()), keyName);

    /// <summary>approval_mode's word: view, key or passkey. An unknown or missing word, device_key included, is View.</summary>
    public static ApprovalMode ModeOf(string? word) => word switch
    {
        "key" => ApprovalMode.Key,
        "passkey" => ApprovalMode.Passkey,
        _ => ApprovalMode.View,
    };
}

/// <summary>Where the kept session lives: Windows' data protection for the current user, in the app.</summary>
public interface ISessionStore
{
    /// <summary>Writes and removes a probe, and throws when the store refuses: a pairing then stops before anything is shown or sent.</summary>
    void CheckWritable();
    void Save(byte[] data);
    byte[]? Load();
    void Delete();
}

/// <summary>Keeping, reading and replacing the session.</summary>
public static class KeptSessions
{
    /// <summary>The path a session signs itself out with (bearer only, as the phones do).</summary>
    public const string LogoutPath = "/v1/auth/device/logout";

    /// <summary>tafs cloud, which asks KIFF who the session is: the one host besides KIFF's API that gets its token.</summary>
    public static readonly Uri TafsCloud = new("https://cloud.tafs.kiff.dev");

    /// <summary>
    /// tafs cloud's streaming function, where a message whose answer comes as it's written goes (<c>…/messages/stream</c>), as
    /// the iPad's <c>TafsCloud.defaultStreamURL</c>: tafs cloud's own, behind the same session check. tafs cloud's main host
    /// has no such route, and answered 404 ("not found") to every message sent from Windows.
    /// </summary>
    public static readonly Uri TafsCloudStream = new("https://ghjfunc5ppretjo22bl5izlnya0zrocm.lambda-url.eu-west-1.on.aws");

    /// <summary>
    /// Whether the session's token may go to <paramref name="url"/>: https, on exactly KIFF's API host, tafs cloud's or tafs
    /// cloud's streaming function's, on the default port, with no user in it. No other subdomain, so a wrong base URL or a
    /// link from an answer never carries the token elsewhere.
    /// </summary>
    public static bool MaySendTo(Uri url, Uri api) =>
        url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps && url.UserInfo.Length == 0 && url.IsDefaultPort
        && new[] { api.Host, TafsCloud.Host, TafsCloudStream.Host }.Contains(url.Host, StringComparer.OrdinalIgnoreCase);

    public static byte[] Write(KeptSession s) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
    {
        ["host"] = s.Host, ["access_token"] = s.AccessToken, ["tenant"] = s.Tenant, ["subject"] = s.Subject,
        ["device_id"] = s.DeviceId, ["approval_mode"] = s.Mode.ToString().ToLowerInvariant(),
        ["expires"] = KiffSigning.Time(s.Expires), ["key_name"] = s.KeyName, ["passkey_id"] = s.PasskeyId,
    });

    /// <summary>
    /// The kept session, if it's for exactly <paramref name="api"/>'s host and hasn't expired; otherwise none.
    /// An unreadable one reads as none.
    /// </summary>
    public static KeptSession? Read(byte[]? data, Uri api, DateTimeOffset now)
    {
        if (data is null) return null;
        try
        {
            var r = JsonDocument.Parse(data).RootElement;
            string S(string name) => r.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
            var kept = new KeptSession(S("host"), S("access_token"), S("tenant"), S("subject"),
                r.TryGetProperty("device_id", out var d) ? d.GetString() : null,
                KeptSession.ModeOf(r.TryGetProperty("approval_mode", out var m) ? m.GetString() : null),
                DateTimeOffset.Parse(S("expires"), System.Globalization.CultureInfo.InvariantCulture), S("key_name"))
            {
                PasskeyId = r.TryGetProperty("passkey_id", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
            };
            return kept.Host == api.Host && kept.Expires > now && kept.AccessToken.Length > 0 ? kept : null;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or InvalidDataException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Keeps <paramref name="next"/> in place of what was kept, and says which session to sign out at KIFF:
    /// the one it replaces, unless it's the same device's. Throws if the store refuses: nothing is shown as done then.
    /// </summary>
    public static KeptSession? Replace(ISessionStore store, Uri api, KeptSession next, DateTimeOffset now)
    {
        var old = Read(store.Load(), api, now);
        store.Save(Write(next));
        if (Read(store.Load(), api, now) is not { } kept || kept.AccessToken != next.AccessToken) throw new IOException("the session wasn't kept");
        return old is not null && old.AccessToken != next.AccessToken && old.KeyName != next.KeyName ? old : null;
    }
}

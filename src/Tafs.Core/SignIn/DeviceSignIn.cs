using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.Pairing;

namespace Tafs.Core.SignIn;

/// <summary>What KIFF answers to a start: the code the person checks in the browser, and where to open it.</summary>
public sealed record SignInCode(
    string DeviceCode, string UserCode, Uri VerificationUri, Uri VerificationUriComplete, DateTimeOffset Expires, TimeSpan Interval)
{
    /// <summary>The device code is a secret until it's used: never printed.</summary>
    public override string ToString() => $"SignInCode {{ UserCode = {UserCode}, Expires = {KiffSigning.Time(Expires)} }}";
}

/// <summary>The session KIFF hands over once, when the person approves in the browser. Its token is never printed.</summary>
public sealed record SignedInSession(string AccessToken, string Tenant, string Subject, TimeSpan ExpiresIn)
{
    public override string ToString() => $"SignedInSession {{ Tenant = {Tenant}, AccessToken = (hidden) }}";
}

public enum SignInStatus { Pending, SlowDown, Expired, SignedIn, Failed }

public sealed record SignInResult(SignInStatus Status, SignedInSession? Session = null, string? Detail = null);

/// <summary>
/// Signing this computer in (KIFF's desktop client contract, §1.1): the device authorization flow the
/// phones use. Start with the public keys, the person approves in a browser, the computer polls for the
/// session. Once an account has an approval device, the session this gives is view-only until the computer
/// is paired. Transport is the caller's; nothing here touches the network.
/// </summary>
public static class DeviceSignIn
{
    /// <summary>KIFF's API (the contract's §1).</summary>
    public static readonly Uri Api = new("https://api.kiff.dev");

    public const string StartPath = "/v1/auth/device/start";
    public const string TokenPath = "/v1/auth/device/token";

    /// <summary>
    /// The start's body. Keys are standard base64 of SPKI DER. With no approval key the computer asks for a
    /// passkey-only session; a device key outside hardware says so and is passkey-only too.
    /// </summary>
    public static byte[] StartBody(byte[] devicePub, byte[]? approvalPub, string name, bool deviceKeyInSoftware = false)
    {
        // The same rules as the pairing offer: P-256, two different keys, KIFF's name limits.
        var offer = PairingOffer.Create(devicePub, approvalPub, name, DateTimeOffset.UnixEpoch, deviceKeyInSoftware: deviceKeyInSoftware);
        var body = new Dictionary<string, string> { ["public_key"] = Convert.ToBase64String(offer.PublicKeyDer) };
        if (offer.ApprovalPublicKeyDer is not null) body["approval_public_key"] = Convert.ToBase64String(offer.ApprovalPublicKeyDer);
        else body["approval"] = "passkey";
        body["device_name"] = offer.Name;
        body["platform"] = "windows";
        body["kind"] = "other";
        if (offer.DeviceKeyInSoftware) body["key_store"] = "software";
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    /// <summary>
    /// Reads the start's answer. Null when it can't be used, including a verification link that isn't
    /// https on KIFF's own host: the app opens it in the browser, so it never opens anything else.
    /// </summary>
    public static SignInCode? ReadStart(int status, string body, DateTimeOffset now, Uri? api = null)
    {
        if (status != 200) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            var uri = new Uri(r.GetProperty("verification_uri").GetString()!);
            var complete = new Uri(r.GetProperty("verification_uri_complete").GetString()!);
            if (!IsKiffLink(uri, api ?? Api) || !IsKiffLink(complete, api ?? Api)) return null;
            var interval = r.TryGetProperty("interval", out var i) ? TimeSpan.FromSeconds(i.GetInt32()) : TimeSpan.Zero;
            return new SignInCode(
                r.GetProperty("device_code").GetString()!,
                r.GetProperty("user_code").GetString()!,
                uri, complete,
                now + TimeSpan.FromSeconds(r.GetProperty("expires_in").GetInt32()),
                interval < MinimumInterval ? MinimumInterval : interval);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or UriFormatException or FormatException or ArgumentNullException)
        {
            return null;
        }
    }

    /// <summary>
    /// An https link on KIFF's own domain: the API's host without its first label (kiff.dev for
    /// api.kiff.dev), or a host under it (KIFF's sign-in page is on app.kiff.dev). So a wrong base URL or a
    /// tampered answer never opens a page somewhere else.
    /// </summary>
    public static bool IsKiffLink(Uri link, Uri api)
    {
        var dot = api.Host.IndexOf('.');
        var domain = dot > 0 && api.Host.IndexOf('.', dot + 1) > 0 ? api.Host[(dot + 1)..] : api.Host;
        return link.IsAbsoluteUri && link.Scheme == Uri.UriSchemeHttps && link.UserInfo.Length == 0 && link.IsDefaultPort
            && (link.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || link.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Why KIFF refused a start, as its status and error code only (invalid_device_name, say).</summary>
    public static string StartRefusal(int status, string body)
    {
        string? error = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e)
                && e.ValueKind == JsonValueKind.String)
                error = e.GetString();
        }
        catch (JsonException) { }
        // Only a short code of letters and underscores: never free text from the answer.
        var code = error is { Length: > 0 and <= 40 } && error.All(c => c is (>= 'a' and <= 'z') or '_') ? " " + error : "";
        return status.ToString(System.Globalization.CultureInfo.InvariantCulture) + code;
    }

    /// <summary>KIFF's default; slow_down adds this much too (RFC 8628).</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(5);

    public static byte[] TokenBody(SignInCode code) =>
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["device_code"] = code.DeviceCode });

    public static SignInResult ReadToken(int status, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            if (status == 200)
                return new SignInResult(SignInStatus.SignedIn, new SignedInSession(
                    r.GetProperty("access_token").GetString()!,
                    r.GetProperty("tenant").GetString()!,
                    r.GetProperty("subject").GetString()!,
                    TimeSpan.FromSeconds(r.GetProperty("expires_in").GetInt64())));

            var error = r.TryGetProperty("error", out var e) ? e.GetString() : null;
            return (status, error) switch
            {
                (400, "authorization_pending") => new SignInResult(SignInStatus.Pending),
                (400, "slow_down") => new SignInResult(SignInStatus.SlowDown),
                (400, "expired_token") => new SignInResult(SignInStatus.Expired),
                _ => new SignInResult(SignInStatus.Failed, Detail: $"{status} {error}"),
            };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Never echo the body: a 200 holds the token.
            return new SignInResult(SignInStatus.Failed, Detail: $"{status}, an answer this app can't read");
        }
    }

    /// <summary>
    /// Polls until the person approves, the code expires, or KIFF refuses: every interval KIFF gave, plus
    /// 5 s on each slow_down. post sends a body to <see cref="TokenPath"/> and returns status and body.
    /// </summary>
    public static async Task<SignInResult> PollAsync(
        SignInCode code,
        Func<byte[], CancellationToken, Task<(int Status, string Body)>> post,
        Func<DateTimeOffset> now,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancel = default)
    {
        var interval = code.Interval;
        while (true)
        {
            await delay(interval, cancel); // the person needs a moment in the browser: wait before the first poll
            if (now() >= code.Expires) return new SignInResult(SignInStatus.Expired);
            var (status, body) = await post(TokenBody(code), cancel);
            var result = ReadToken(status, body);
            switch (result.Status)
            {
                case SignInStatus.Pending: break;
                case SignInStatus.SlowDown: interval += MinimumInterval; break;
                default: return result;
            }
        }
    }
}

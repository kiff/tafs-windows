using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.Words;

namespace Tafs.Core.Usage;

/// <summary>
/// One of the two acts KIFF takes only signed by the device key (<c>KIFF-Device-Signature</c>), never as a passkey's approval:
/// Raise the limit and Decline a member's request. Its request is fixed when it's made, before Windows Hello is asked, and
/// never changes until it's sent: what the person confirms is what's signed. Each try makes a new one at a new
/// <c>signed_at</c>.
/// </summary>
public sealed class DeviceSignedRequest
{
    public const string RaisePath = "/v1/me/usage/limit";

    DeviceSignedRequest(string tenant, string method, string path, byte[] body, string signedAt) =>
        (Tenant, Method, Path, this.body, SignedAt) = (tenant, method, path, body, signedAt);

    public string Tenant { get; }
    public string Method { get; }
    public string Path { get; }
    public string SignedAt { get; }

    /// <summary>The exact body bytes sent, which <see cref="Message"/> hashes: a copy, so the request can't change once made.</summary>
    public byte[] Body => body.ToArray();
    readonly byte[] body;

    /// <summary>
    /// <c>raiseLimit(from:)</c>: <c>PUT /v1/me/usage/limit</c>, <c>{"raise":true,"from_cents":N}</c>, the limit raised one
    /// step from the one it is now, as tafs cloud's offer names it. None without a tenant or with a negative limit.
    /// </summary>
    public static DeviceSignedRequest? Raise(string tenant, long fromCents, string signedAt) =>
        tenant.Length == 0 || fromCents < 0 ? null
        : new(tenant, "PUT", RaisePath, JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["raise"] = true, ["from_cents"] = fromCents }), signedAt);

    /// <summary>
    /// <c>declineRequest(_:note:)</c>: <c>POST /v1/me/pack-requests/{id}/decline</c>, <c>{"note"}</c>, the note as it stood when
    /// Decline was pressed. None without a tenant or a request.
    /// </summary>
    public static DeviceSignedRequest? Decline(string tenant, string request, string note, string signedAt) =>
        tenant.Length == 0 || request.Length == 0 ? null : new(tenant, "POST", KiffUsage.DeclinePath(request), KiffUsage.DeclineBody(note), signedAt);

    /// <summary>KIFF's <c>kiff.request.v1</c> over this request: what the device key signs, and KIFF checks.</summary>
    public byte[] Message => KiffSigning.RequestV1(Tenant, Method, Path, body, SignedAt);

    /// <summary>
    /// What Windows Hello's passkey answers over, never sent anywhere: <c>tafs.presence.request.v1</c>, the tenant, <c>signed_at</c>
    /// and the SHA-256 of <see cref="Message"/>. Its first line keeps it from ever being a message KIFF takes as an approval:
    /// the challenge is never the request's own.
    /// </summary>
    public byte[] PresenceMessage => KiffSigning.Message("tafs.presence.request.v1", Tenant, SignedAt, KiffSigning.Sha256Hex(Message));
}

/// <summary>
/// That this PC's Windows Hello checked the person for exactly one <see cref="DeviceSignedRequest"/>, just now. Only
/// <see cref="Check"/> makes one, from a passkey answer it has verified; it signs once, only its own request, and only within
/// <see cref="Life"/> of that request's <c>signed_at</c>. It can't be copied, made otherwise, or used again.
/// </summary>
public sealed class Presence
{
    /// <summary>KIFF's window for a request's <c>signed_at</c>: older, the person confirms again.</summary>
    public static readonly TimeSpan Life = TimeSpan.FromSeconds(60);

    readonly byte[] bound;
    bool spent;

    Presence(byte[] bound) => this.bound = bound;

    /// <summary>
    /// Checks this PC's passkey answer over <paramref name="request"/>'s <see cref="DeviceSignedRequest.PresenceMessage"/>, as KIFF
    /// checks an assertion (kiff.dev's, present and verified, never a passkey that can leave this PC), from the credential asked
    /// (an answer that names another is refused; one that names none is the one asked), and by its signature against
    /// <paramref name="publicKeySpki"/>, the key kept when this PC paired, never a key from the answer. Throws
    /// <see cref="PasskeyRefusedException"/> otherwise.
    /// </summary>
    public static Presence Check(DeviceSignedRequest request, PasskeyAssertion answer, byte[] credentialAsked, byte[] publicKeySpki)
    {
        var head = AuthenticatorData.Parse(answer.AuthenticatorData);
        if (PasskeyApprover.Refusal(head) is { } refusal) throw new PasskeyRefusedException(refusal);
        if (answer.CredentialId is { Length: > 0 } id && !id.AsSpan().SequenceEqual(credentialAsked))
            throw new PasskeyRefusedException("another passkey answered");
        // What a WebAuthn assertion signs: the authenticator data, then sha256 of the clientDataJSON this app built.
        var clientData = PasskeyApprover.ClientDataJson(request.PresenceMessage);
        byte[] signed = [.. answer.AuthenticatorData, .. SHA256.HashData(clientData)];
        bool holds;
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKeySpki, out var read);
            holds = read == publicKeySpki.Length && KiffSigning.IsP256Spki(publicKeySpki)
                && key.VerifyData(signed, answer.SignatureDer, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            holds = false;
        }
        if (!holds) throw new PasskeyRefusedException("the passkey's signature doesn't hold");
        return new Presence(SHA256.HashData(request.PresenceMessage));
    }

    /// <summary>
    /// The device key's signature over <paramref name="request"/>'s <c>kiff.request.v1</c>, as KIFF reads the header
    /// (<c>t=&lt;signed_at&gt;,v1=&lt;base64 DER&gt;</c>), only when this presence is that request's and its <c>signed_at</c> is
    /// within <see cref="Life"/> of <paramref name="now"/>. Spent either way: a mismatch never signs another request after.
    /// </summary>
    public async Task<string> SignAsync(DeviceSignedRequest request, Func<byte[], Task<byte[]>> signDer, DateTimeOffset now)
    {
        if (spent) throw new PasskeyRefusedException("this confirmation was used");
        spent = true;
        if (!SHA256.HashData(request.PresenceMessage).AsSpan().SequenceEqual(bound)) throw new PasskeyRefusedException("the confirmation is another request's");
        if (!DateTimeOffset.TryParse(request.SignedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) || now - at > Life || at - now > Life)
            throw new PasskeyRefusedException("the confirmation is too old");
        return KiffSigning.SignatureHeaderV1(request.SignedAt, await signDer(request.Message));
    }
}

/// <summary>How a device-signed act ended on this side: sent (KIFF's answer), closed by the person, not confirmed, or stopped by a sign-out.</summary>
public enum DeviceSentKind { Sent, Cancelled, Unconfirmed, Stopped }

public sealed record DeviceSent(DeviceSentKind Kind, int Status = 0, string Body = "");

/// <summary>
/// A device-signed act, in this order and no other: the request made (fixed), then Windows Hello's passkey asked over its
/// presence message and the answer checked (<see cref="Presence.Check"/>), then the device key signs, then the request is sent
/// to KIFF, once. Closing Windows Hello sends nothing; anything not confirmed sends nothing; a sign-out meanwhile stops it.
/// </summary>
public static class DeviceSigning
{
    public static async Task<DeviceSent> SendAsync(DeviceSignedRequest request, IPasskeyAuthenticator hello, byte[] credentialId, byte[] publicKeySpki,
        Func<byte[], Task<byte[]>> signDer, Func<DeviceSignedRequest, string, Task<(int Status, string Body)>> send, Func<DateTimeOffset> now,
        Func<bool> current, CancellationToken cancel = default)
    {
        Presence presence;
        try
        {
            var answer = await hello.GetAssertionAsync(PasskeyApprover.RpId, credentialId, PasskeyApprover.ClientDataJson(request.PresenceMessage), cancel);
            if (!current()) return new(DeviceSentKind.Stopped);
            presence = Presence.Check(request, answer, credentialId, publicKeySpki);
        }
        catch (OperationCanceledException)
        {
            return new(current() ? DeviceSentKind.Cancelled : DeviceSentKind.Stopped);
        }
        catch (Exception e) when (e is PasskeyRefusedException or InvalidDataException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException
            or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            return new(current() ? DeviceSentKind.Unconfirmed : DeviceSentKind.Stopped);
        }
        string header;
        try { header = await presence.SignAsync(request, signDer, now()); }
        catch (Exception e) when (e is PasskeyRefusedException or CryptographicException or System.Runtime.InteropServices.COMException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            return new(current() ? DeviceSentKind.Unconfirmed : DeviceSentKind.Stopped);
        }
        if (!current()) return new(DeviceSentKind.Stopped);
        int status;
        string body;
        try { (status, body) = await send(request, header); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { (status, body) = (0, ""); }
        return current() ? new(DeviceSentKind.Sent, status, body) : new(DeviceSentKind.Stopped);
    }

    /// <summary>
    /// What the person reads after a raise: nothing when it was raised or Windows Hello was closed; KIFF's 409s in the app's own
    /// words (the limit changed meanwhile, or is as high as the app may raise it); else that it wasn't raised, with why.
    /// </summary>
    public static string? RaiseSaid(WordBook w, DeviceSent sent) => sent.Kind switch
    {
        DeviceSentKind.Sent when sent.Status is >= 200 and < 300 => null,
        DeviceSentKind.Sent when sent.Status == 409 && ErrorCode(sent.Body) == "limit_changed" => w.Get("usage.limit_changed"),
        DeviceSentKind.Sent when sent.Status == 409 && ErrorCode(sent.Body) == "limit_ceiling" => w.Get("usage.limit_ceiling"),
        DeviceSentKind.Sent => w.Get("usage.raise_limit.not_raised", ("why", KiffErrors.Words(w, sent.Status))),
        DeviceSentKind.Unconfirmed => w.Get("common.not_sent_unconfirmed"),
        _ => null,
    };

    /// <summary>What the person reads after a decline: nothing when declined or closed; else that it wasn't declined, with why.</summary>
    public static string? DeclineSaid(WordBook w, DeviceSent sent) => sent.Kind switch
    {
        DeviceSentKind.Sent when sent.Status is >= 200 and < 300 => null,
        DeviceSentKind.Sent => w.Get("billing.request.not_declined", ("why", KiffErrors.Words(w, sent.Status))),
        DeviceSentKind.Unconfirmed => w.Get("common.not_sent_unconfirmed"),
        _ => null,
    };

    /// <summary>KIFF's error code (<c>{"error":"limit_changed"}</c>), or none.</summary>
    static string? ErrorCode(string body)
    {
        try
        {
            using var d = JsonDocument.Parse(body);
            return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}

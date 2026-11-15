using System.Net.Http;
using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Kiff;

/// <summary>How setting up planque ended: its key kept, enrolled with a new one, or why not.</summary>
public enum EnrolOutcome { Kept, Enrolled, AdminOnly, OtherDevice, NotSetUp }

/// <summary>What setting up planque came to, with the person's words for why when it didn't.</summary>
public sealed record EnrolResult(EnrolOutcome Outcome, string? Why = null)
{
    public bool Ready => Outcome is EnrolOutcome.Kept or EnrolOutcome.Enrolled;
}

/// <summary>
/// Setting this device up with planque, once per account, as the iPad's <c>setUpPlanque</c> and <c>enrolPlanque</c> (with
/// iOS #1106 and planque#248): planque is asked first with no key, and keeps the connector key it holds while KIFF still
/// takes it (<c>kept</c>). Only otherwise is a KIFF key minted, which connects tools and does nothing else, and handed to
/// planque; it's never kept here. Every call to planque carries this device's signature, never KIFF's token.
/// </summary>
public static class PlanqueEnrolment
{
    public static string Path(string tenant) => $"/v1/accounts/{CloudRead.PathPart(tenant)}/enrolment";

    /// <summary>KIFF's keys: minting one is a setup call KIFF takes only signed by this device.</summary>
    public const string KeysPath = "/v1/keys";

    /// <summary>What the device confirms before setting up, as the iPad's Face ID prompt signs it: <c>tafs.planque.v1|{tenant}</c>.</summary>
    public static byte[] GateMessage(string tenant) => System.Text.Encoding.UTF8.GetBytes($"tafs.planque.v1|{tenant}");

    /// <summary>The key KIFF mints for planque: labelled <c>planque</c>, with the <c>connector</c> role only.</summary>
    public static byte[] MintBody() => JsonSerializer.SerializeToUtf8Bytes(new { label = "planque", roles = new[] { "connector" } });

    /// <summary>
    /// <c>KIFF-Device-Signature</c> for the mint, as a signed setup call: <c>kiff.request.v1</c> over exactly
    /// <see cref="MintBody"/>, signed by the session's device key, which asks no one.
    /// </summary>
    public static async Task<string> MintSignatureAsync(IKiffSigner key, string tenant, DateTimeOffset signedAt, CancellationToken cancel = default) =>
        KiffSigning.SignatureHeaderV1(signedAt, await key.SignDerAsync(KiffSigning.RequestV1(tenant, "POST", KeysPath, MintBody(), signedAt), cancel));

    /// <summary>
    /// The enrolment: this device's key (SPKI DER, base64), for a planque from before device ids, and the connector key when
    /// there is one. With none, it only asks whether the enrolled key still stands.
    /// </summary>
    public static byte[] Body(byte[] deviceKeyDer, string? connectorKey)
    {
        var body = new Dictionary<string, string> { ["device_key"] = Convert.ToBase64String(deviceKeyDer) };
        if (connectorKey is not null) body["connector_key"] = connectorKey;
        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    /// <summary>
    /// Sets planque up, as the iPad's <c>enrolPlanque</c>: asked first with no key, a 2xx with <c>kept: true</c> is done and
    /// mints nothing. Anything else (<c>needs_key</c>, a planque from before, not reached) mints one key and enrols with it.
    /// KIFF refusing the key (403) is for an admin only; planque refusing the enrolment (409) is another device's; anything
    /// else says why. <paramref name="postToPlanque"/> signs and sends a body; <paramref name="mintKey"/> sends KIFF's signed
    /// mint. Each answers status and body, and throws <see cref="HttpRequestException"/> when not reached.
    /// </summary>
    public static async Task<EnrolResult> EnrolAsync(WordBook w, byte[] deviceKeyDer, Func<byte[], Task<(int Status, string Body)>> postToPlanque,
        Func<Task<(int Status, string Body)>> mintKey)
    {
        try
        {
            var (status, body) = await postToPlanque(Body(deviceKeyDer, null));
            if (status is >= 200 and <= 299 && Kept(body)) return new(EnrolOutcome.Kept);
        }
        catch (HttpRequestException) { }
        string key;
        try
        {
            var (status, body) = await mintKey();
            if (status == 403) return new(EnrolOutcome.AdminOnly, w.Get("coach.connect_admin_only"));
            if (status is < 200 or > 299 || Plaintext(body) is not { } minted)
                return NotSetUp(w, CloudRead.Refused(w, status, status is < 200 or > 299 ? body : "").Why);
            key = minted;
        }
        catch (HttpRequestException) { return NotSetUp(w, CloudRead.Unreached(w).Why); }
        try
        {
            var (status, body) = await postToPlanque(Body(deviceKeyDer, key));
            if (status is >= 200 and <= 299) return new(EnrolOutcome.Enrolled);
            if (status == 409) return new(EnrolOutcome.OtherDevice, w.Get("coach.connect_other_device"));
            return NotSetUp(w, CloudRead.Refused(w, status, body).Why);
        }
        catch (HttpRequestException) { return NotSetUp(w, CloudRead.Unreached(w).Why); }
    }

    static EnrolResult NotSetUp(WordBook w, string? why) => new(EnrolOutcome.NotSetUp, w.Get("coach.connecting_not_set_up", ("why", why ?? "")));

    /// <summary>planque's answer says it kept the connector key it holds.</summary>
    static bool Kept(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("kept", out var k) && k.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>The minted key's words, from KIFF's answer: held only until planque has it.</summary>
    static string? Plaintext(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return Lenient.Text(doc.RootElement, "plaintext") is { Length: > 0 } p ? p : null;
        }
        catch (JsonException) { return null; }
    }
}

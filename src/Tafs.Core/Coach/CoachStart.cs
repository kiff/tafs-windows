using System.Net.Http;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>How Start ended: Coach started, or why not.</summary>
public enum StartOutcome { Started, CantApprove, Cancelled, FirstStart, Failed }

/// <summary>What Start came to, with the person's words for why when it didn't, and Coach's id once it's there.</summary>
public sealed record StartResult(StartOutcome Outcome, string? Why = null, string? Coach = null)
{
    public bool Started => Outcome == StartOutcome.Started;
}

/// <summary>
/// What Start sends, and how, given by the app and faked in tests. <see cref="SignDraft"/> signs a draft: with the
/// device key for a key session, which asks no one, or with this PC's passkey for a passkey session, which is its one
/// Windows Hello. <see cref="SignTogether"/> signs tafs cloud's pair once with the device key (<see cref="RequestsTogether"/>);
/// <see cref="SendBatch"/> sends its batch to KIFF's gateway. <see cref="Confirm"/> asks Windows Hello over a message.
/// Each send throws <see cref="HttpRequestException"/> when not reached.
/// </summary>
public sealed record StartCalls(
    Func<Task<CloudRead>> ReadSetup,
    Func<byte[], byte[], Task> Confirm,
    Func<byte[], DateTimeOffset, Task<(string Header, string Value)>> SignDraft,
    Func<StartRequest, StartRequest, DateTimeOffset, Task<(string Batch, string Draft)>> SignTogether,
    Func<Task<EnrolResult>> Enrol,
    Func<byte[], (string Header, string Value), Task<(int Status, string Body)>> SendBatch,
    Func<byte[], (string Header, string Value), Task<(int Status, string Body)>> SubmitDraft,
    Func<byte[], Task<CloudRead>> Claim,
    Func<DateTimeOffset> Now);

/// <summary>
/// Starting Coach, as the iPad's <c>AppModel.startSetup</c> from Start (<c>SetupChatView.introBefore</c>): planque first, as
/// its <c>setUpPlanque</c>, then Setup from a draft with no tools, whose key tafs cloud claims, so the key never reaches this
/// PC. Behind one Windows Hello for a passkey session, as the iPad's one Face ID; none for a key session, as its other
/// setup calls; never for a view session.
/// </summary>
public static class CoachStart
{
    public const string SetupPath = "/v1/setup";
    public const string DraftPath = "/v1/me/agent-drafts";

    /// <summary>KIFF's gateway, as the iPad's <c>KIFFClient.gatewayURL</c>: where Coach's tools batch goes, with the session's token.</summary>
    public static readonly Uri Gateway = new("https://mcp.kiff.dev");

    /// <summary>Whether a call may go to the gateway: https, on exactly its host, on the default port, with no user in it.</summary>
    public static bool IsGateway(Uri url) =>
        url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps && url.UserInfo.Length == 0 && url.IsDefaultPort
        && string.Equals(url.Host, Gateway.Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>Setup's agent in KIFF, as the iPad's <c>AppModel.setupAgent</c>.</summary>
    public const string SetupAgent = "setup";

    /// <summary>What Start confirms, as the iPad's Face ID prompt signs it: <c>tafs.setup.v1|{tenant}</c>.</summary>
    public static byte[] Message(string tenant) => System.Text.Encoding.UTF8.GetBytes($"tafs.setup.v1|{tenant}");

    /// <summary>Setup's draft, as the iPad's <c>submitNoToolsDraft(agentID: "setup", label: "Setup")</c>: an agent that holds no tools.</summary>
    public static byte[] DraftBody() => JsonSerializer.SerializeToUtf8Bytes(new { agent_id = SetupAgent, label = "Setup", no_tools = true });

    /// <summary>tafs cloud's claim of the draft's key, as the iPad's <c>TafsCloud.startSetup</c>.</summary>
    public static byte[] ClaimBody(string draftId, string claimSecret) =>
        JsonSerializer.SerializeToUtf8Bytes(new { draft_id = draftId, claim_secret = claimSecret });

    /// <summary>
    /// Setup's draft signed as an approval: <c>KIFF-Device-Signature</c> by the device key for a key session (this PC's one
    /// key), or <c>KIFF-Passkey-Assertion</c> by this PC's passkey for a passkey session, which shows Windows Hello. Each over
    /// exactly <paramref name="body"/> at <paramref name="signedAt"/>, which is what's sent.
    /// </summary>
    public static async Task<(string Header, string Value)> SignDraftAsync(KeptSession session, IKiffSigner key, PasskeyApprover? passkey,
        byte[] body, DateTimeOffset signedAt, CancellationToken cancel = default)
    {
        if (session.Mode == ApprovalMode.Passkey)
        {
            if (passkey is null) throw new PasskeyRefusedException("this session doesn't know its passkey");
            return (PasskeyApprover.HeaderName, await passkey.ApproveAsync(session.Tenant, "POST", DraftPath, body, signedAt, cancel));
        }
        var message = KiffSigning.RequestV1(session.Tenant, "POST", DraftPath, body, signedAt);
        return ("KIFF-Device-Signature", KiffSigning.SignatureHeaderV1(signedAt, await key.SignDerAsync(message, cancel)));
    }

    /// <summary>Whether this session may start Coach at all: a view session can't approve, nor a passkey session that doesn't know its passkey.</summary>
    public static bool MayStart(KeptSession session) =>
        session.Mode == ApprovalMode.Key || (session.Mode == ApprovalMode.Passkey && session.PasskeyId is not null);

    /// <summary>
    /// Starts Coach, in the iPad's order: planque first unless it's ready, then, with no Coach yet, Setup's draft and tafs
    /// cloud's claim of its key. The one confirmation comes before anything is sent: for a passkey session, Windows Hello
    /// asserting Setup's draft itself, built in full first (as the iPad's prepared requests), or, with Coach already there,
    /// over <see cref="Message"/>. <c>/v1/setup</c> is read before it, so nothing is asked for what won't be sent.
    /// With tafs cloud's first start (<see cref="FirstStart"/>, as the iPad's <c>startCoachTogether</c>), its draft goes in
    /// place of Setup's: alone, signed as Setup's is; or with the tools batch, both signed once by the device key
    /// (<c>kiff.requests.v1</c>), the batch first and the draft once the batch answered. A passkey session can't sign the
    /// pair (<see cref="StartOutcome.FirstStart"/>): nothing is asked or sent.
    /// </summary>
    public static async Task<StartResult> StartAsync(WordBook w, KeptSession session, bool planqueReady, StartCalls calls)
    {
        if (!MayStart(session)) return new(StartOutcome.CantApprove, w.Get("approval.device_can_not_approve_approve"));
        var read = await calls.ReadSetup();
        if (read.Answer is not { } setup) return Failed(w, read.Why);
        var coach = Lenient.Text(Lenient.Field(setup, "setup") ?? default, "id");
        if (coach is not null && planqueReady) return new(StartOutcome.Started, Coach: coach);
        var first = coach is null && FirstStart.Read(setup) is { Startable: true } f ? f : null;
        // Signed only as Start's own screen draws it (its words, Coach's Cards in KIFF's): the tools step that shows them
        // otherwise isn't on this PC yet. And kiff.requests.v1 is one approval-key signature: a passkey's assertion can't be it.
        if (first is { OneScreen: false } || (first is { Together: true } && session.Mode != ApprovalMode.Key)) return new(StartOutcome.FirstStart);

        byte[]? draft = null;
        (string Header, string Value) signature = default;
        (string Batch, string Draft)? together = null;
        if (coach is null)
        {
            draft = first?.Draft!.Body ?? DraftBody();
            try
            {
                if (first is { Together: true }) together = await calls.SignTogether(first.Connect!, first.Draft!, calls.Now());
                else signature = await calls.SignDraft(draft, calls.Now());
            }
            catch (OperationCanceledException) { return new(StartOutcome.Cancelled); }
            catch (PasskeyRefusedException e) { return Failed(w, e.Message); }
        }
        else if (await PlanqueEnrolment.GateAsync(w, session, calls.Confirm, Message(session.Tenant)) is { } stopped)
        {
            return stopped.Outcome == EnrolOutcome.Cancelled ? new(StartOutcome.Cancelled) : new(StartOutcome.Failed, stopped.Why);
        }

        if (!planqueReady && await calls.Enrol() is { Ready: false } enrol) return new(StartOutcome.Failed, enrol.Why);
        if (coach is not null) return new(StartOutcome.Started, Coach: coach);

        int status;
        string body;
        if (together is { } pair)
        {
            // The batch first; the draft only once it answered, as the iPad's sendTogether.
            try { (status, body) = await calls.SendBatch(first!.Connect!.Body, (DeviceSignature, pair.Batch)); }
            catch (HttpRequestException) { return Failed(w, CloudRead.Unreached(w).Why); }
            if (status is < 200 or > 299) return Failed(w, CloudRead.Refused(w, status, body).Why);
            signature = (DeviceSignature, pair.Draft);
        }
        try { (status, body) = await calls.SubmitDraft(draft!, signature); }
        catch (HttpRequestException) { return Failed(w, CloudRead.Unreached(w).Why); }
        if (status is < 200 or > 299) return Failed(w, CloudRead.Refused(w, status, body).Why);
        if (Drafted(body) is not { } made) return new(StartOutcome.Failed, w.Get("kiff.kiff_did_not_answer_expected"));
        var claimed = await calls.Claim(ClaimBody(made.Id, made.Secret));
        if (claimed.Answer is not { } c) return Failed(w, claimed.Why);
        return Lenient.Text(Lenient.Field(c, "setup") ?? default, "id") is { } started
            ? new(StartOutcome.Started, Coach: started)
            : Failed(w, w.Get("error.ours"));
    }

    const string DeviceSignature = "KIFF-Device-Signature";

    static StartResult Failed(WordBook w, string? why) => new(StartOutcome.Failed, w.Get("coach.couldnt_start", ("why", why ?? "")));

    /// <summary>The draft's id and its claim secret, shown once in KIFF's answer to the submit.</summary>
    static (string Id, string Secret)? Drafted(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return Lenient.Text(doc.RootElement, "id") is { Length: > 0 } id && Lenient.Text(doc.RootElement, "claim_secret") is { Length: > 0 } secret
                ? (id, secret) : null;
        }
        catch (JsonException) { return null; }
    }
}

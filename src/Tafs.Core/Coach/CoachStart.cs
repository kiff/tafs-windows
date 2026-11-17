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
/// What Start sends, and how, given by the app and faked in tests. <see cref="SignDraft"/> signs a draft with this PC's
/// passkey, which is its one Windows Hello. <see cref="SignTogether"/> signs tafs cloud's pair once, the same way
/// (<see cref="RequestsTogether"/>); <see cref="SendBatch"/> sends its batch
/// to KIFF's gateway. <see cref="Confirm"/> asks Windows Hello over a message. Each send throws
/// <see cref="HttpRequestException"/> when not reached. <see cref="SignedAt"/> is <see cref="SigningTime.Next"/>'s time, kept as
/// written. <see cref="Current"/> says whether the session Start began in is still signed in.
/// </summary>
public sealed record StartCalls(
    Func<Task<CloudRead>> ReadSetup,
    Func<byte[], byte[], Task> Confirm,
    Func<byte[], string, Task<(string Header, string Value)>> SignDraft,
    Func<StartRequest, StartRequest, string, Task<(string Header, string Batch, string Draft)>> SignTogether,
    Func<Task<EnrolResult>> Enrol,
    Func<byte[], (string Header, string Value), Task<(int Status, string Body)>> SendBatch,
    Func<byte[], (string Header, string Value), Task<(int Status, string Body)>> SubmitDraft,
    Func<byte[], Task<CloudRead>> Claim,
    Func<string> SignedAt,
    Func<bool> Current,
    Func<Task>? KeepAlerts = null);

/// <summary>
/// Starting Coach, as the iPad's <c>AppModel.startSetup</c> from Start (<c>SetupChatView.introBefore</c>): planque first, as
/// its <c>setUpPlanque</c>, then Setup from a draft with no tools, whose key tafs cloud claims, so the key never reaches this
/// PC. Behind one Windows Hello, as the iPad's one Face ID: only a passkey session that knows its passkey starts Coach. A key
/// session's device key asks no one, so it never gives the authority Start gives; a view session can't approve.
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
    /// Setup's draft signed as an approval, as the iPad's under its biometric Face ID: <c>KIFF-Passkey-Assertion</c> by this
    /// PC's passkey, which shows Windows Hello, over exactly <paramref name="body"/> at <paramref name="signedAt"/>, which is
    /// what's sent. Anything but a passkey session that knows its passkey fails closed: nothing is signed.
    /// </summary>
    public static async Task<(string Header, string Value)> SignDraftAsync(KeptSession session, PasskeyApprover? passkey,
        byte[] body, string signedAt, CancellationToken cancel = default)
    {
        if (session.Mode != ApprovalMode.Passkey || passkey is null) throw new PasskeyRefusedException("only this PC's passkey signs the draft");
        return (PasskeyApprover.HeaderName, await passkey.ApproveAsync(session.Tenant, "POST", DraftPath, body, signedAt, cancel));
    }

    /// <summary>Whether this session may start Coach at all: only a passkey session that knows its passkey.</summary>
    public static bool MayStart(KeptSession session) => session.Mode == ApprovalMode.Passkey && session.PasskeyId is not null;

    /// <summary>
    /// KIFF's refusal as the iPad's <c>KIFFClient.send</c> reads it: a 401 is signed out of KIFF; otherwise KIFF's <c>error</c>,
    /// else "KIFF answered {status}". Never tafs cloud's reading.
    /// </summary>
    public static string KiffRefused(WordBook w, int status, string body)
    {
        if (status == 401) return w.Get("error.signed_out");
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (Lenient.Text(doc.RootElement, "error") is { Length: > 0 } error) return error;
        }
        catch (JsonException) { }
        return w.Get("error.kiff_answered", ("status", status.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Starts Coach, in the iPad's order: planque first unless it's ready, then, with no Coach yet, Setup's draft and tafs
    /// cloud's claim of its key. The one confirmation comes before anything is sent: for a passkey session, Windows Hello
    /// asserting Setup's draft itself, built in full first (as the iPad's prepared requests), or, with Coach already there,
    /// over <see cref="Message"/>. <c>/v1/setup</c> is read before it, so nothing is asked for what won't be sent.
    /// With tafs cloud's first start (<see cref="FirstStart"/>, as the iPad's <c>startCoachTogether</c>), its draft goes in
    /// place of Setup's: alone, signed as Setup's is; or with the tools batch, both signed once (<c>kiff.requests.v1</c>, by
    /// this PC's passkey, its one Windows Hello, in KIFF's <c>KIFF-Passkey-Assertion</c> with <c>with=</c>), the batch first
    /// and the draft once the batch answered.
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
        // otherwise isn't on this PC yet.
        if (first is { OneScreen: false }) return new(StartOutcome.FirstStart);

        byte[]? draft = null;
        (string Header, string Value) signature = default;
        (string Header, string Batch, string Draft)? together = null;
        // Windows Hello closed by the person: "Cancelled. Nothing changed.", as Refuse and Your step say it. One that doesn't
        // verify, or a sign-out meanwhile, stays quiet, as before.
        var closed = false;
        StartResult Stopped() => new(StartOutcome.Cancelled, closed && calls.Current() ? w.Get("approval.cancelled") : null);
        async Task Hello(byte[] id, byte[] message)
        {
            try { await calls.Confirm(id, message); }
            catch (OperationCanceledException) { closed = true; throw; }
        }
        if (coach is null)
        {
            draft = first?.Draft!.Body ?? DraftBody();
            // A Windows Hello closed or not verified stops quietly, as the iPad's failed Face ID: nothing is sent.
            try
            {
                if (first is { Together: true }) together = await calls.SignTogether(first.Connect!, first.Draft!, calls.SignedAt());
                else signature = await calls.SignDraft(draft, calls.SignedAt());
            }
            catch (OperationCanceledException)
            {
                closed = true;
                return Stopped();
            }
            catch (PasskeyRefusedException) { return new(StartOutcome.Cancelled); }
        }
        else if (await PlanqueEnrolment.GateAsync(w, session, Hello, Message(session.Tenant)) is { } stopped)
        {
            return stopped.Outcome == EnrolOutcome.Cancelled ? Stopped() : new(StartOutcome.Failed, stopped.Why);
        }
        // Signed out meanwhile: nothing more is sent, as the iPad's accountGeneration checks after each await.
        if (!calls.Current()) return new(StartOutcome.Cancelled);

        // KIFF's alerts kept with tafs cloud, behind the Windows Hello just asked, as the iPad's keepKIFFAlerts before planque.
        // Best effort: nothing here stops Start.
        if (calls.KeepAlerts is { } keepAlerts)
        {
            // Whatever goes wrong keeping them, Start goes on: only a cancellation is Start's own to stop for.
            try { await keepAlerts(); }
            catch (Exception e) when (e is not OperationCanceledException) { }
        }
        if (!calls.Current()) return new(StartOutcome.Cancelled);

        if (!planqueReady)
        {
            var enrol = await calls.Enrol();
            if (!calls.Current()) return new(StartOutcome.Cancelled);
            if (!enrol.Ready) return new(StartOutcome.Failed, enrol.Why);
        }
        if (coach is not null) return new(StartOutcome.Started, Coach: coach);

        int status;
        string body;
        if (together is { } pair)
        {
            // The batch first; the draft only once it answered, as the iPad's sendTogether.
            try { (status, body) = await calls.SendBatch(first!.Connect!.Body, (pair.Header, pair.Batch)); }
            catch (HttpRequestException) { return Failed(w, CloudRead.Unreached(w).Why); }
            if (!calls.Current()) return new(StartOutcome.Cancelled);
            if (status is < 200 or > 299) return Failed(w, KiffRefused(w, status, body));
            signature = (pair.Header, pair.Draft);
        }
        try { (status, body) = await calls.SubmitDraft(draft!, signature); }
        catch (HttpRequestException) { return Failed(w, CloudRead.Unreached(w).Why); }
        if (!calls.Current()) return new(StartOutcome.Cancelled);
        if (status is < 200 or > 299) return Failed(w, KiffRefused(w, status, body));
        // No claim secret: tafs cloud's first start wraps it in "Couldn't start.", as the iPad's catch; Setup's own draft says it bare.
        if (Drafted(body) is not { } made)
            return first is null ? new(StartOutcome.Failed, w.Get("kiff.kiff_did_not_answer_expected")) : Failed(w, w.Get("kiff.kiff_did_not_answer_expected"));
        var claimed = await calls.Claim(ClaimBody(made.Id, made.Secret));
        if (claimed.Answer is not { } c) return Failed(w, claimed.Why);
        return Lenient.Text(Lenient.Field(c, "setup") ?? default, "id") is { } started
            ? new(StartOutcome.Started, Coach: started)
            : Failed(w, w.Get("error.ours"));
    }

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

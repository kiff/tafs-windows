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
/// What Start sends, and how, given by the app and faked in tests. <see cref="SignDraft"/> signs Setup's draft with this PC's
/// passkey, which is its one Windows Hello. <see cref="Confirm"/> asks Windows Hello over a message. Each send throws
/// <see cref="HttpRequestException"/> when not reached. <see cref="SignedAt"/> is <see cref="SigningTime.Next"/>'s time, kept as
/// written. <see cref="Current"/> says whether the session Start began in is still signed in.
/// </summary>
public sealed record StartCalls(
    Func<Task<CloudRead>> ReadSetup,
    Func<byte[], byte[], Task> Confirm,
    Func<byte[], string, Task<(string Header, string Value)>> SignDraft,
    Func<Task<EnrolResult>> Enrol,
    Func<byte[], (string Header, string Value), Task<(int Status, string Body)>> SubmitDraft,
    Func<byte[], Task<CloudRead>> Claim,
    Func<string> SignedAt,
    Func<bool> Current);

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
    /// tafs cloud's own first start (<c>start</c>, the tools and Coach's Cards signed together) isn't on this PC yet: it
    /// says so as <see cref="StartOutcome.FirstStart"/>, and nothing is asked or sent.
    /// </summary>
    public static async Task<StartResult> StartAsync(WordBook w, KeptSession session, bool planqueReady, StartCalls calls)
    {
        if (!MayStart(session)) return new(StartOutcome.CantApprove, w.Get("approval.device_can_not_approve_approve"));
        var read = await calls.ReadSetup();
        if (read.Answer is not { } setup) return Failed(w, read.Why);
        var coach = Lenient.Text(Lenient.Field(setup, "setup") ?? default, "id");
        if (coach is not null && planqueReady) return new(StartOutcome.Started, Coach: coach);
        if (coach is null && Startable(setup)) return new(StartOutcome.FirstStart);

        byte[]? draft = null;
        (string Header, string Value) signature = default;
        if (coach is null)
        {
            draft = DraftBody();
            // A Windows Hello closed or not verified stops quietly, as the iPad's failed Face ID: nothing is sent.
            try { signature = await calls.SignDraft(draft, calls.SignedAt()); }
            catch (Exception e) when (e is OperationCanceledException or PasskeyRefusedException) { return new(StartOutcome.Cancelled); }
        }
        else if (await PlanqueEnrolment.GateAsync(w, session, calls.Confirm, Message(session.Tenant)) is { } stopped)
        {
            return stopped.Outcome == EnrolOutcome.Cancelled ? new(StartOutcome.Cancelled) : new(StartOutcome.Failed, stopped.Why);
        }
        // Signed out meanwhile: nothing more is sent, as the iPad's accountGeneration checks after each await.
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
        try { (status, body) = await calls.SubmitDraft(draft!, signature); }
        catch (HttpRequestException) { return Failed(w, CloudRead.Unreached(w).Why); }
        if (!calls.Current()) return new(StartOutcome.Cancelled);
        if (status is < 200 or > 299) return Failed(w, KiffRefused(w, status, body));
        if (Drafted(body) is not { } made) return new(StartOutcome.Failed, w.Get("kiff.kiff_did_not_answer_expected"));
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

    /// <summary>
    /// tafs cloud's first start, as the iPad's <c>CoachStart.startable</c>: <c>to_give</c>, with Coach's Cards' draft (a
    /// <c>POST</c> to <c>/v1/me/agent-drafts</c>, with its Cards' words), alone under <c>kiff.request.v1</c> or with the tools batch under
    /// <c>kiff.requests.v1</c>. Only before Coach exists.
    /// </summary>
    public static bool Startable(JsonElement setup)
    {
        if (Lenient.Field(setup, "start") is not { ValueKind: JsonValueKind.Object } start || Lenient.Text(start, "state") != "to_give") return false;
        if (Lenient.Field(start, "requests") is not { ValueKind: JsonValueKind.Array } requests) return false;
        JsonElement? Request(string id) => requests.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object && Lenient.Text(r, "id") == id)
            .Select(r => (JsonElement?)r).FirstOrDefault();
        static bool Gateway(JsonElement r) => r.TryGetProperty("gateway", out var g) && g.ValueKind == JsonValueKind.True;
        if (Request("draft") is not { } draft || Lenient.Text(draft, "method") != "POST" || Lenient.Text(draft, "path") != DraftPath || Gateway(draft)) return false;
        // Never Cards the person hasn't read in KIFF's own words.
        if (Lenient.Field(draft, "cards") is not { ValueKind: JsonValueKind.Array } cards || cards.GetArrayLength() == 0) return false;
        var scheme = Lenient.Text(start, "scheme");
        if (Request("connect") is { } connect)
            return scheme == "kiff.requests.v1" && Lenient.Text(connect, "method") == "POST" && Lenient.Text(connect, "path") == "/v1/connections/batch" && Gateway(connect);
        return scheme == "kiff.request.v1";
    }
}

using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Coach;
using Tafs.Core.Contracts;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>
/// Start, as the iPad's <c>startSetup</c>: who may start, the one Windows Hello before anything is sent, planque first, then
/// Setup's draft and tafs cloud's claim of its key; and what stops it.
/// </summary>
[Part("approvals")]
public class CoachStartTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static readonly DateTimeOffset At = new(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);

    static KeptSession Session(ApprovalMode mode, string? passkey = null) =>
        new KeptSession("api.kiff.dev", "token", "t_01", "s1", "dev_pc01", mode, DateTimeOffset.MaxValue, "k") { PasskeyId = passkey };

    static CloudRead Json(string json) => CloudRead.Of(JsonDocument.Parse(json).RootElement.Clone());

    const string NoCoach = """{"setup": null, "tools": null}""";
    const string HasCoach = """{"setup": {"id": "taf_coach", "name": "Coach"}}""";

    /// <summary>Every call Start makes, in order, each answering as set.</summary>
    sealed class Stubs
    {
        public readonly List<string> Calls = [];
        public CloudRead Setup = Json(NoCoach);
        public Func<Task>? Hello;
        public EnrolResult Enrolled = new(EnrolOutcome.Enrolled);
        public (int, string) Draft = (201, """{"id": "drf_1", "agent_id": "setup", "status": "confirmed", "version": "v1", "claim_secret": "cs_1"}""");
        public CloudRead Claimed = Json("""{"setup": {"id": "taf_coach"}}""");
        public (int, string) Batch = (200, "{}");
        public byte[]? DraftSent, ClaimSent, BatchSent;
        public (string, string) Signature, BatchSignature;
        /// <summary>Signs out right after this call, by name: nothing after it may be sent.</summary>
        public string? SignOutAfter;
        bool current = true;

        void Did(string call)
        {
            Calls.Add(call);
            if (call == SignOutAfter) current = false;
        }

        public StartCalls Calls_() => new(
            () => { Did("read"); return Task.FromResult(Setup); },
            async (id, message) => { if (Hello is { } h) await h(); Did($"hello {Encoding.UTF8.GetString(message)}"); },
            async (body, at) =>
            {
                if (Hello is { } h) await h();
                Did("sign draft");
                return ("KIFF-Passkey-Assertion", $"signed {Encoding.UTF8.GetString(body)} at {at}");
            },
            async (connect, draft, at) =>
            {
                if (Hello is { } h) await h();
                Did($"sign together {connect.Id} {draft.Id}");
                return ("KIFF-Passkey-Assertion", $"batch at {at}", $"draft at {at}");
            },
            () => { Did("enrol"); return Task.FromResult(Enrolled); },
            (body, signature) => { Did("batch"); (BatchSent, BatchSignature) = (body, signature); return Task.FromResult(Batch); },
            (body, signature) => { Did("draft"); (DraftSent, Signature) = (body, signature); return Task.FromResult(Draft); },
            body => { Did("claim"); ClaimSent = body; return Task.FromResult(Claimed); },
            () => "2026-11-15T18:00:00.123Z",
            () => current);

        public Task<StartResult> Run(KeptSession session, bool planqueReady = false) =>
            CoachStart.StartAsync(En, session, planqueReady, Calls_());
    }

    /// <summary>The iPad's order: planque first, then Setup's draft, signed before anything is sent, then tafs cloud claims its key.</summary>
    [Fact]
    public async Task Start_sets_planque_up_first_then_Setup_s_draft_and_its_key()
    {
        var stubs = new Stubs();
        Assert.Equal(new StartResult(StartOutcome.Started, Coach: "taf_coach"), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));

        Assert.Equal(["read", "sign draft", "enrol", "draft", "claim"], stubs.Calls);
        Assert.Equal("""{"agent_id":"setup","label":"Setup","no_tools":true}""", Encoding.UTF8.GetString(stubs.DraftSent!));
        Assert.Equal(("KIFF-Passkey-Assertion", """signed {"agent_id":"setup","label":"Setup","no_tools":true} at 2026-11-15T18:00:00.123Z"""), stubs.Signature);
        Assert.Equal("""{"draft_id":"drf_1","claim_secret":"cs_1"}""", Encoding.UTF8.GetString(stubs.ClaimSent!));

        // planque already set up this session: no enrolment.
        stubs = new Stubs();
        Assert.True((await stubs.Run(Session(ApprovalMode.Passkey, "AQI"), planqueReady: true)).Started);
        Assert.Equal(["read", "sign draft", "draft", "claim"], stubs.Calls);
    }

    /// <summary>
    /// With Coach already there, only planque is left: the passkey session confirms over tafs.setup.v1|{tenant} with its own
    /// passkey, and with planque ready nothing is asked or sent.
    /// </summary>
    [Fact]
    public async Task With_Coach_there_only_planque_is_left()
    {
        var stubs = new Stubs { Setup = Json(HasCoach) };
        Assert.Equal(new StartResult(StartOutcome.Started, Coach: "taf_coach"), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.Equal(["read", "hello tafs.setup.v1|t_01", "enrol"], stubs.Calls);

        stubs = new Stubs { Setup = Json(HasCoach) };
        Assert.True((await stubs.Run(Session(ApprovalMode.Passkey, "AQI"), planqueReady: true)).Started);
        Assert.Equal(["read"], stubs.Calls);
    }

    /// <summary>
    /// A view session, a key session (its device key asks no one, so it never gives Start's authority), or a passkey session
    /// that doesn't know its passkey, can't approve: nothing is read, asked or sent, for setup-start.json too.
    /// </summary>
    [Fact]
    public async Task A_session_that_can_t_approve_starts_nothing()
    {
        foreach (var session in new[] { Session(ApprovalMode.View), Session(ApprovalMode.Key), Session(ApprovalMode.Passkey) })
        {
            var stubs = new Stubs();
            Assert.Equal(new StartResult(StartOutcome.CantApprove, "This device can't approve. Approve on another device signed in to your account."),
                await stubs.Run(session));
            Assert.Empty(stubs.Calls);
            Assert.False(CoachStart.MayStart(session));
        }
        var start = new Stubs { Setup = CloudRead.Of(ContractFile.Answer(SharedFiles.Contract("setup-start.json"))) };
        Assert.Equal(StartOutcome.CantApprove, (await start.Run(Session(ApprovalMode.Key))).Outcome);
        Assert.Empty(start.Calls);
        Assert.True(CoachStart.MayStart(Session(ApprovalMode.Passkey, "AQI")));
    }

    /// <summary>
    /// Closing Windows Hello, or one that doesn't verify, stops quietly, as the iPad's failed Face ID: no developer words, and
    /// planque and KIFF are sent nothing.
    /// </summary>
    [Fact]
    public async Task Windows_Hello_closed_or_refused_sends_nothing()
    {
        var stubs = new Stubs { Hello = () => throw new OperationCanceledException() };
        Assert.Equal(new StartResult(StartOutcome.Cancelled), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.Equal(["read"], stubs.Calls);

        stubs = new Stubs { Hello = () => throw new PasskeyRefusedException("Windows Hello didn't verify the person") };
        Assert.Equal(new StartResult(StartOutcome.Cancelled), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.Equal(["read"], stubs.Calls);

        stubs = new Stubs { Setup = Json(HasCoach), Hello = () => throw new OperationCanceledException() };
        Assert.Equal(new StartResult(StartOutcome.Cancelled), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.Equal(["read"], stubs.Calls);
    }

    /// <summary>A sign-out during Windows Hello, during planque's enrolment, or before the claim sends nothing after it.</summary>
    [Fact]
    public async Task A_sign_out_meanwhile_sends_nothing_after_it()
    {
        foreach (var (after, sent) in new[] { ("sign draft", new[] { "read", "sign draft" }), ("enrol", ["read", "sign draft", "enrol"]),
            ("draft", ["read", "sign draft", "enrol", "draft"]) })
        {
            var stubs = new Stubs { SignOutAfter = after };
            Assert.Equal(new StartResult(StartOutcome.Cancelled), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
            Assert.Equal(sent, stubs.Calls);
        }
        var coach = new Stubs { Setup = Json(HasCoach), SignOutAfter = "hello tafs.setup.v1|t_01" };
        Assert.Equal(new StartResult(StartOutcome.Cancelled), await coach.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.Equal(["read", "hello tafs.setup.v1|t_01"], coach.Calls);
    }

    /// <summary>What stops Start says why, and what comes after it isn't sent: planque not set up, KIFF's refusal, an answer with no claim secret, tafs cloud's refusal.</summary>
    [Fact]
    public async Task What_stops_Start_says_why()
    {
        var stubs = new Stubs { Enrolled = new(EnrolOutcome.AdminOnly, "Only an admin can connect apps.") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "Only an admin can connect apps."), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.Equal(["read", "sign draft", "enrol"], stubs.Calls);

        stubs = new Stubs { Draft = (403, """{"error": "only an admin can give keys", "words": "tafs cloud's words"}""") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "Couldn't start. only an admin can give keys"), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.DoesNotContain("claim", stubs.Calls);

        // KIFF's refusal as the iPad's KIFFClient.send reads it: a body-less 500 is "KIFF answered 500", a 401 signed out.
        stubs = new Stubs { Draft = (500, "") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "Couldn't start. KIFF answered 500"), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        stubs = new Stubs { Draft = (401, """{"error": "token expired"}""") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "Couldn't start. Signed out of KIFF. Sign in again."), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));

        stubs = new Stubs { Draft = (201, """{"id": "drf_1", "status": "draft"}""") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "KIFF didn't answer as expected. Try again."), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.DoesNotContain("claim", stubs.Calls);

        stubs = new Stubs { Claimed = CloudRead.Refused(En, 409, """{"error": "already claimed"}""") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "Couldn't start. already claimed"), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));

        stubs = new Stubs { Setup = CloudRead.Unreached(En) };
        Assert.Equal(StartOutcome.Failed, (await stubs.Run(Session(ApprovalMode.Passkey, "AQI"))).Outcome);
        Assert.Equal(["read"], stubs.Calls);
    }

    static JsonElement Contract(string file) => ContractFile.Answer(SharedFiles.Contract(file));

    static System.Text.Json.Nodes.JsonObject Node(JsonElement e) => System.Text.Json.Nodes.JsonNode.Parse(e.GetRawText())!.AsObject();

    /// <summary>
    /// tafs cloud's first start, as the iPad's startCoachTogether, from the contract at the pin: the pair signed once before
    /// anything is sent, planque, the batch to the gateway, then the draft once the batch answered, each as tafs cloud wrote
    /// it, then tafs cloud's claim.
    /// </summary>
    [Fact]
    public async Task tafs_cloud_s_first_start_sends_the_pair_signed_once()
    {
        var answer = Contract("setup-start.json");
        var first = FirstStart.Read(answer)!;
        var stubs = new Stubs { Setup = CloudRead.Of(answer) };
        Assert.Equal(new StartResult(StartOutcome.Started, Coach: "taf_coach"), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));

        Assert.Equal(["read", "sign together connect draft", "enrol", "batch", "draft", "claim"], stubs.Calls);
        Assert.Equal(first.Connect!.Body, stubs.BatchSent);
        Assert.Equal(("KIFF-Passkey-Assertion", "batch at 2026-11-15T18:00:00.123Z"), stubs.BatchSignature);
        Assert.Equal(first.Draft!.Body, stubs.DraftSent);
        Assert.Equal(("KIFF-Passkey-Assertion", "draft at 2026-11-15T18:00:00.123Z"), stubs.Signature);

        // The batch refused, as KIFF's send reads it: the draft isn't sent.
        stubs = new Stubs { Setup = CloudRead.Of(answer), Batch = (409, """{"error": "a tool named tafs_state exists", "words": "not KIFF's"}""") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "Couldn't start. a tool named tafs_state exists"), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.Equal(["read", "sign together connect draft", "enrol", "batch"], stubs.Calls);
        stubs = new Stubs { Setup = CloudRead.Of(answer), Batch = (502, "") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "Couldn't start. KIFF answered 502"), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));

        // A sign-out after the pair's signature, the enrolment, the batch or the draft: nothing more is sent.
        foreach (var (after, sent) in new[] { ("sign together connect draft", new[] { "read", "sign together connect draft" }),
            ("enrol", ["read", "sign together connect draft", "enrol"]), ("batch", ["read", "sign together connect draft", "enrol", "batch"]),
            ("draft", ["read", "sign together connect draft", "enrol", "batch", "draft"]) })
        {
            stubs = new Stubs { Setup = CloudRead.Of(answer), SignOutAfter = after };
            Assert.Equal(new StartResult(StartOutcome.Cancelled), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
            Assert.Equal(sent, stubs.Calls);
        }

        // A key session never signs the pair: it reads, asks and sends nothing.
        stubs = new Stubs { Setup = CloudRead.Of(answer) };
        Assert.Equal(StartOutcome.CantApprove, (await stubs.Run(Session(ApprovalMode.Key))).Outcome);
        Assert.Empty(stubs.Calls);
    }

    /// <summary>
    /// With every tool connected, Coach's draft goes alone, signed as Setup's is: the passkey session's one Windows Hello. A
    /// draft with no claim secret says "Couldn't start." with why, as the iPad's catch.
    /// </summary>
    [Fact]
    public async Task With_every_tool_connected_the_draft_goes_alone()
    {
        var answer = Contract("setup-start-draft-alone.json");
        var stubs = new Stubs { Setup = CloudRead.Of(answer) };
        Assert.True((await stubs.Run(Session(ApprovalMode.Passkey, "AQI"))).Started);
        Assert.Equal(["read", "sign draft", "enrol", "draft", "claim"], stubs.Calls);
        Assert.Equal(FirstStart.Read(answer)!.Draft!.Body, stubs.DraftSent);

        stubs = new Stubs { Setup = CloudRead.Of(answer), Draft = (201, """{"id": "drf_1", "status": "draft"}""") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "Couldn't start. KIFF didn't answer as expected. Try again."), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
    }

    /// <summary>
    /// Closing Windows Hello on the pair sends nothing. A first start Start's screen doesn't draw (no words: the iPad's tools
    /// step, not on this PC yet) asks and sends nothing; one that isn't to give is Setup's draft, as before.
    /// </summary>
    [Fact]
    public async Task Closing_Hello_on_the_pair_and_an_undrawn_start_send_nothing()
    {
        var stubs = new Stubs { Setup = CloudRead.Of(Contract("setup-start.json")), Hello = () => throw new OperationCanceledException() };
        Assert.Equal(new StartResult(StartOutcome.Cancelled), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.Equal(["read"], stubs.Calls);

        var json = Node(Contract("setup-start.json"));
        json["start"]!.AsObject().Remove("words");
        stubs = new Stubs { Setup = Json(json.ToJsonString()) };
        Assert.Equal(new StartResult(StartOutcome.FirstStart), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.Equal(["read"], stubs.Calls);

        var given = new Stubs { Setup = Json("""{"setup": null, "start": {"state": "given"}}""") };
        Assert.True((await given.Run(Session(ApprovalMode.Passkey, "AQI"))).Started);
        Assert.Equal("""{"agent_id":"setup","label":"Setup","no_tools":true}""", Encoding.UTF8.GetString(given.DraftSent!));
    }

    /// <summary>
    /// The first start read as the iPad's CoachStart.read: each request's body as tafs cloud wrote it, the batch's lines,
    /// KIFF's words for the Cards, the step's groups and details, Start's words; startable as the iPad's.
    /// </summary>
    [Fact]
    public void The_first_start_reads_as_the_iPad_s()
    {
        var answer = Contract("setup-start.json");
        var first = FirstStart.Read(answer)!;
        Assert.Equal(("to_give", "kiff.requests.v1", true, true, true), (first.State, first.Scheme, first.Together, first.Startable, first.OneScreen));
        var raw = answer.GetProperty("start").GetProperty("requests");
        Assert.Equal(raw[0].GetProperty("body").GetRawText(), Encoding.UTF8.GetString(first.Connect!.Body));
        Assert.Equal(raw[1].GetProperty("body").GetRawText(), Encoding.UTF8.GetString(first.Draft!.Body));
        Assert.Equal("See your tafs, their versions and their runs", first.Connect.Lines[0]);
        Assert.Equal(("Coach's Card", "KIFF's line for coach-reads"), (first.Draft.CardTitle, first.Draft.Cards[0].Lines[0]));
        Assert.Equal(("Watch your tafs", "See your tafs"), (first.Groups[0].Name, first.Groups[0].Tools[0].Name));
        Assert.Equal("Each tool and its limits", first.Details);
        Assert.Equal(new FirstStartWords("Start gives Coach the tools below. You approve them on this device.", "Start", ""), first.Words);

        var alone = FirstStart.Read(Contract("setup-start-draft-alone.json"))!;
        Assert.False(alone.Together);
        Assert.True(alone.Startable);
        Assert.Null(alone.Connect);
        Assert.Null(FirstStart.Read(Contract("setup-coach.json")));

        // Never Cards the person hasn't read in KIFF's words: a draft without them isn't startable.
        var json = Node(answer);
        foreach (var r in json["start"]!["requests"]!.AsArray()) r!.AsObject().Remove("cards");
        Assert.False(FirstStart.Read(JsonDocument.Parse(json.ToJsonString()).RootElement)!.Startable);
    }

    /// <summary>
    /// kiff.requests.v1, as the iPad's RequestsTogether: the scheme, the account, the one signed_at as written (milliseconds
    /// kept) and the two request messages' hashes in sorted order, signed once by this PC's passkey, its challenge
    /// base64url(sha256) of that message; each header carries the other's hash.
    /// </summary>
    [Fact]
    public async Task The_pair_is_signed_once_over_both_requests()
    {
        const string at = "2026-11-15T18:00:00.123Z";
        using var key = new TestSigner();
        var first = FirstStart.Read(Contract("setup-start.json"))!;
        var batchHash = RequestsTogether.Hash(KiffSigning.RequestV1("t_01", "POST", "/v1/connections/batch", first.Connect!.Body, at));
        var draftHash = RequestsTogether.Hash(KiffSigning.RequestV1("t_01", "POST", "/v1/me/agent-drafts", first.Draft!.Body, at));
        var sorted = string.CompareOrdinal(batchHash, draftHash) <= 0 ? new[] { batchHash, draftHash } : [draftHash, batchHash];
        var message = RequestsTogether.Message("t_01", at, batchHash, draftHash);
        Assert.Equal(string.Join((char)10, ["kiff.requests.v1", "t_01", at, .. sorted]), Encoding.UTF8.GetString(message));
        Assert.Equal(message, RequestsTogether.Message("t_01", at, draftHash, batchHash));
        Assert.Equal(64, batchHash.Length);

        var hello = new Hello(key);
        var (header, batch, draft) = await RequestsTogether.SignAsync(new PasskeyApprover(hello, [1, 2], true), "t_01", first.Connect!, first.Draft!, at);
        Assert.Equal("KIFF-Passkey-Assertion", header);
        Assert.StartsWith($"t={at},a=", batch);
        Assert.EndsWith($",with=sha256:{draftHash}", batch);
        Assert.EndsWith($",with=sha256:{batchHash}", draft);
        Assert.Equal(batch.Split(",with=")[0], draft.Split(",with=")[0]);
        using var client = JsonDocument.Parse(hello.ClientData!);
        Assert.Equal(PasskeyApprover.Challenge(message), client.RootElement.GetProperty("challenge").GetString());
    }

    /// <summary>
    /// Setup's draft is signed as an approval, over exactly what's sent, only by this PC's passkey, its challenge bound to that
    /// request at signed_at as written, milliseconds and all: two signatures in the same second carry different t=. Anything
    /// but a passkey session fails closed.
    /// </summary>
    [Fact]
    public async Task Setup_s_draft_is_signed_as_an_approval()
    {
        using var key = new TestSigner();
        var body = CoachStart.DraftBody();
        var hello = new Hello(key);
        var passkey = new PasskeyApprover(hello, [1, 2], true);
        var (header, value) = await CoachStart.SignDraftAsync(Session(ApprovalMode.Passkey, "AQI"), passkey, body, "2026-11-15T18:00:00.123Z");
        Assert.Equal("KIFF-Passkey-Assertion", header);
        Assert.StartsWith("t=2026-11-15T18:00:00.123Z,a=", value);
        using (var client = JsonDocument.Parse(hello.ClientData!))
            Assert.Equal(PasskeyApprover.Challenge(KiffSigning.RequestV1("t_01", "POST", "/v1/me/agent-drafts", body, "2026-11-15T18:00:00.123Z")),
                client.RootElement.GetProperty("challenge").GetString());
        var (_, next) = await CoachStart.SignDraftAsync(Session(ApprovalMode.Passkey, "AQI"), passkey, body, "2026-11-15T18:00:00.124Z");
        Assert.StartsWith("t=2026-11-15T18:00:00.124Z,a=", next);

        await Assert.ThrowsAsync<PasskeyRefusedException>(() => CoachStart.SignDraftAsync(Session(ApprovalMode.Passkey), null, body, "t"));
        await Assert.ThrowsAsync<PasskeyRefusedException>(() => CoachStart.SignDraftAsync(Session(ApprovalMode.Key), passkey, body, "t"));
        await Assert.ThrowsAsync<PasskeyRefusedException>(() => CoachStart.SignDraftAsync(Session(ApprovalMode.View), passkey, body, "t"));
        Assert.Equal("tafs.setup.v1|t_01", Encoding.UTF8.GetString(CoachStart.Message("t_01")));
    }

    /// <summary>Stands in for Windows Hello: this PC's own passkey, with presence and verification.</summary>
    sealed class Hello(TestSigner key) : IPasskeyAuthenticator
    {
        public byte[]? ClientData;

        public async Task<PasskeyAssertion> GetAssertionAsync(string rpId, byte[] credentialId, byte[] clientDataJson, CancellationToken cancel)
        {
            ClientData = clientDataJson;
            var authData = SHA256.HashData(Encoding.UTF8.GetBytes(rpId)).Concat(new byte[] { 0x05, 0, 0, 0, 1 }).ToArray();
            return new PasskeyAssertion(authData, await key.SignDerAsync(authData.Concat(SHA256.HashData(clientDataJson)).ToArray(), cancel));
        }
    }
}

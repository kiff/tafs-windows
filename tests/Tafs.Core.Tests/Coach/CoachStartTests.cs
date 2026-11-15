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
        public byte[]? DraftSent, ClaimSent;
        public (string, string) Signature;

        public StartCalls Calls_() => new(
            () => { Calls.Add("read"); return Task.FromResult(Setup); },
            async (id, message) => { Calls.Add($"hello {Encoding.UTF8.GetString(message)}"); if (Hello is { } h) await h(); },
            async (body, at) =>
            {
                Calls.Add("sign draft");
                if (Hello is { } h) await h();
                return ("KIFF-Device-Signature", $"signed {Encoding.UTF8.GetString(body)} at {KiffSigning.Time(at)}");
            },
            () => { Calls.Add("enrol"); return Task.FromResult(Enrolled); },
            (body, signature) => { Calls.Add("draft"); (DraftSent, Signature) = (body, signature); return Task.FromResult(Draft); },
            body => { Calls.Add("claim"); ClaimSent = body; return Task.FromResult(Claimed); },
            () => At);

        public Task<StartResult> Run(KeptSession session, bool planqueReady = false) => CoachStart.StartAsync(En, session, planqueReady, Calls_());
    }

    /// <summary>The iPad's order: planque first, then Setup's draft, signed before anything is sent, then tafs cloud claims its key.</summary>
    [Fact]
    public async Task Start_sets_planque_up_first_then_Setup_s_draft_and_its_key()
    {
        var stubs = new Stubs();
        Assert.Equal(new StartResult(StartOutcome.Started, Coach: "taf_coach"), await stubs.Run(Session(ApprovalMode.Key)));

        Assert.Equal(["read", "sign draft", "enrol", "draft", "claim"], stubs.Calls);
        Assert.Equal("""{"agent_id":"setup","label":"Setup","no_tools":true}""", Encoding.UTF8.GetString(stubs.DraftSent!));
        Assert.Equal(("KIFF-Device-Signature", """signed {"agent_id":"setup","label":"Setup","no_tools":true} at 2026-11-15T18:00:00Z"""), stubs.Signature);
        Assert.Equal("""{"draft_id":"drf_1","claim_secret":"cs_1"}""", Encoding.UTF8.GetString(stubs.ClaimSent!));

        // planque already set up this session: no enrolment.
        stubs = new Stubs();
        Assert.True((await stubs.Run(Session(ApprovalMode.Key), planqueReady: true)).Started);
        Assert.Equal(["read", "sign draft", "draft", "claim"], stubs.Calls);
    }

    /// <summary>
    /// With Coach already there, only planque is left: a passkey session confirms over tafs.setup.v1|{tenant} with its own
    /// passkey, a key session asks no one, and with planque ready nothing is asked or sent.
    /// </summary>
    [Fact]
    public async Task With_Coach_there_only_planque_is_left()
    {
        var stubs = new Stubs { Setup = Json(HasCoach) };
        Assert.Equal(new StartResult(StartOutcome.Started, Coach: "taf_coach"), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.Equal(["read", "hello tafs.setup.v1|t_01", "enrol"], stubs.Calls);

        stubs = new Stubs { Setup = Json(HasCoach) };
        Assert.True((await stubs.Run(Session(ApprovalMode.Key))).Started);
        Assert.Equal(["read", "enrol"], stubs.Calls);

        stubs = new Stubs { Setup = Json(HasCoach) };
        Assert.True((await stubs.Run(Session(ApprovalMode.Passkey, "AQI"), planqueReady: true)).Started);
        Assert.Equal(["read"], stubs.Calls);
    }

    /// <summary>A view session, or a passkey session that doesn't know its passkey, can't approve: nothing is read, asked or sent.</summary>
    [Fact]
    public async Task A_session_that_can_t_approve_starts_nothing()
    {
        foreach (var session in new[] { Session(ApprovalMode.View), Session(ApprovalMode.Passkey) })
        {
            var stubs = new Stubs();
            Assert.Equal(new StartResult(StartOutcome.CantApprove, "This device can't approve. Approve on another device signed in to your account."),
                await stubs.Run(session));
            Assert.Empty(stubs.Calls);
            Assert.False(CoachStart.MayStart(session));
        }
        Assert.True(CoachStart.MayStart(Session(ApprovalMode.Key)));
        Assert.True(CoachStart.MayStart(Session(ApprovalMode.Passkey, "AQI")));
    }

    /// <summary>Closing Windows Hello stops quietly, and a refusal says why: either way planque and KIFF are sent nothing.</summary>
    [Fact]
    public async Task Windows_Hello_closed_or_refused_sends_nothing()
    {
        var stubs = new Stubs { Hello = () => throw new OperationCanceledException() };
        Assert.Equal(new StartResult(StartOutcome.Cancelled), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.Equal(["read", "sign draft"], stubs.Calls);

        stubs = new Stubs { Hello = () => throw new PasskeyRefusedException("Windows Hello didn't verify the person") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "Couldn't start. Windows Hello didn't verify the person"), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.Equal(["read", "sign draft"], stubs.Calls);

        stubs = new Stubs { Setup = Json(HasCoach), Hello = () => throw new OperationCanceledException() };
        Assert.Equal(new StartResult(StartOutcome.Cancelled), await stubs.Run(Session(ApprovalMode.Passkey, "AQI")));
        Assert.Equal(["read", "hello tafs.setup.v1|t_01"], stubs.Calls);
    }

    /// <summary>What stops Start says why, and what comes after it isn't sent: planque not set up, KIFF's refusal, an answer with no claim secret, tafs cloud's refusal.</summary>
    [Fact]
    public async Task What_stops_Start_says_why()
    {
        var stubs = new Stubs { Enrolled = new(EnrolOutcome.AdminOnly, "Only an admin can connect apps.") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "Only an admin can connect apps."), await stubs.Run(Session(ApprovalMode.Key)));
        Assert.Equal(["read", "sign draft", "enrol"], stubs.Calls);

        stubs = new Stubs { Draft = (403, """{"error": "only the owner can give keys"}""") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "Couldn't start. only the owner can give keys"), await stubs.Run(Session(ApprovalMode.Key)));
        Assert.DoesNotContain("claim", stubs.Calls);

        stubs = new Stubs { Draft = (201, """{"id": "drf_1", "status": "draft"}""") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "KIFF didn't answer as expected. Try again."), await stubs.Run(Session(ApprovalMode.Key)));
        Assert.DoesNotContain("claim", stubs.Calls);

        stubs = new Stubs { Claimed = CloudRead.Refused(En, 409, """{"error": "already claimed"}""") };
        Assert.Equal(new StartResult(StartOutcome.Failed, "Couldn't start. already claimed"), await stubs.Run(Session(ApprovalMode.Key)));

        stubs = new Stubs { Setup = CloudRead.Unreached(En) };
        Assert.Equal(StartOutcome.Failed, (await stubs.Run(Session(ApprovalMode.Key))).Outcome);
        Assert.Equal(["read"], stubs.Calls);
    }

    /// <summary>
    /// tafs cloud's own first start (the tools and Coach's Cards signed together, or the draft alone) isn't on this PC yet:
    /// read from both contracts at the pin, nothing is asked or sent. A start that isn't to give is Setup's draft as before.
    /// </summary>
    [Fact]
    public async Task tafs_cloud_s_first_start_asks_and_sends_nothing_yet()
    {
        foreach (var file in new[] { "setup-start.json", "setup-start-draft-alone.json" })
        {
            var answer = ContractFile.Answer(SharedFiles.Contract(file));
            Assert.True(CoachStart.Startable(answer), file);
            var stubs = new Stubs { Setup = CloudRead.Of(answer) };
            Assert.Equal(new StartResult(StartOutcome.FirstStart), await stubs.Run(Session(ApprovalMode.Key)));
            Assert.Equal(["read"], stubs.Calls);
        }
        Assert.False(CoachStart.Startable(ContractFile.Answer(SharedFiles.Contract("setup-coach.json"))));
        Assert.False(CoachStart.Startable(JsonDocument.Parse("""{"start": {"state": "given", "scheme": "kiff.request.v1", "requests": []}}""").RootElement));
        var given = new Stubs { Setup = Json("""{"setup": null, "start": {"state": "given"}}""") };
        Assert.True((await given.Run(Session(ApprovalMode.Key))).Started);
    }

    /// <summary>
    /// Setup's draft is signed as an approval, over exactly what's sent: by the device key for a key session, by this PC's
    /// passkey for a passkey session, its challenge bound to that request.
    /// </summary>
    [Fact]
    public async Task Setup_s_draft_is_signed_as_an_approval()
    {
        using var key = new TestSigner();
        var body = CoachStart.DraftBody();
        var (header, value) = await CoachStart.SignDraftAsync(Session(ApprovalMode.Key), key, null, body, At);
        Assert.Equal("KIFF-Device-Signature", header);
        Assert.StartsWith("t=2026-11-15T18:00:00Z,v1=", value);
        Assert.True(TestSigner.Verify(key.PublicKeyDer, KiffSigning.RequestV1("t_01", "POST", "/v1/me/agent-drafts", body, At), Convert.FromBase64String(value.Split(",v1=")[1])));

        var hello = new Hello(key);
        (header, value) = await CoachStart.SignDraftAsync(Session(ApprovalMode.Passkey, "AQI"), key, new PasskeyApprover(hello, [1, 2], true), body, At);
        Assert.Equal("KIFF-Passkey-Assertion", header);
        Assert.StartsWith("t=2026-11-15T18:00:00Z,a=", value);
        using var client = JsonDocument.Parse(hello.ClientData!);
        Assert.Equal(PasskeyApprover.Challenge(KiffSigning.RequestV1("t_01", "POST", "/v1/me/agent-drafts", body, At)),
            client.RootElement.GetProperty("challenge").GetString());

        await Assert.ThrowsAsync<PasskeyRefusedException>(() => CoachStart.SignDraftAsync(Session(ApprovalMode.Passkey), key, null, body, At));
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

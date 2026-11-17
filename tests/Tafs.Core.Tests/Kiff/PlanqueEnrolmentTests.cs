using System.Net.Http;
using System.Text;
using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Kiff;

/// <summary>
/// Setting this device up with planque, as the iPad's setUpPlanque and enrolPlanque: planque asked first
/// with no key, a key minted only when planque needs one. Nothing here reaches planque or KIFF: both are stubs.
/// </summary>
[Part("approvals")]
public class PlanqueEnrolmentTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static readonly byte[] DeviceKey = [0x30, 0x59, 0x01];

    /// <summary>planque and KIFF as stubs: each answer in turn, and what was sent.</summary>
    sealed class Stubs(params (int, string)[] planque)
    {
        readonly Queue<(int, string)> answers = new(planque);
        public List<Dictionary<string, string>> Enrolments { get; } = [];
        public int Mints { get; private set; }
        public (int, string) Mint { get; init; } = (201, """{"id": "key_c", "plaintext": "kiff_live_connector", "roles": ["connector"]}""");
        public bool PlanqueUnreached { get; init; }
        public bool KiffUnreached { get; init; }

        public Task<(int, string)> Post(byte[] body)
        {
            Enrolments.Add(JsonSerializer.Deserialize<Dictionary<string, string>>(body)!);
            if (PlanqueUnreached && Enrolments.Count == 1) throw new HttpRequestException("unreached");
            return Task.FromResult(answers.Dequeue());
        }

        public Task<(int, string)> MintKey()
        {
            Mints++;
            if (KiffUnreached) throw new HttpRequestException("unreached");
            return Task.FromResult(Mint);
        }

        /// <summary>Signed out after this many sends (planque's asks and the mint, counted together); none, never.</summary>
        public int? SignOutAfter { get; init; }

        bool Current() => SignOutAfter is not { } n || Enrolments.Count + Mints < n;

        public Task<EnrolResult> Run() => PlanqueEnrolment.EnrolAsync(En, DeviceKey, Post, MintKey, Current);
    }

    /// <summary>
    /// A sign-out between the sends stops there, as the iPad's enrolPlanque checks accountGeneration after each: after
    /// planque's first ask, no key is minted; after the mint, none is enrolled; after the enrolment, it's still cancelled.
    /// </summary>
    [Fact]
    public async Task A_sign_out_between_the_sends_mints_and_enrols_nothing_more()
    {
        const string needsKey = """{"error": "needs_key", "code": "needs_key"}""";
        var afterAsk = new Stubs((409, needsKey), (201, "{}")) { SignOutAfter = 1 };
        Assert.Equal(EnrolOutcome.Cancelled, (await afterAsk.Run()).Outcome);
        Assert.Equal((1, 0), (afterAsk.Enrolments.Count, afterAsk.Mints));

        var afterMint = new Stubs((409, needsKey), (201, "{}")) { SignOutAfter = 2 };
        Assert.Equal(EnrolOutcome.Cancelled, (await afterMint.Run()).Outcome);
        Assert.Equal((1, 1), (afterMint.Enrolments.Count, afterMint.Mints));

        var afterEnrol = new Stubs((409, needsKey), (201, "{}")) { SignOutAfter = 3 };
        Assert.Equal(EnrolOutcome.Cancelled, (await afterEnrol.Run()).Outcome);
        Assert.Equal((2, 1), (afterEnrol.Enrolments.Count, afterEnrol.Mints));

        // Signed in throughout: enrolled.
        Assert.Equal(EnrolOutcome.Enrolled, (await new Stubs((409, needsKey), (201, "{}")).Run()).Outcome);
    }

    /// <summary>KIFF's refusal of the mint is read as KIFF's: its error, else "KIFF answered 500", and a 401 as signed out.</summary>
    [Fact]
    public async Task KIFF_s_refusal_of_the_mint_is_read_as_KIFF_s()
    {
        const string needsKey = """{"error": "needs_key", "code": "needs_key"}""";
        Assert.Contains("no keys today", (await new Stubs((409, needsKey)) { Mint = (500, """{"error": "no keys today", "words": "not KIFF's"}""") }.Run()).Why);
        Assert.Contains(En.Get("error.kiff_answered", ("status", "500")), (await new Stubs((409, needsKey)) { Mint = (500, "") }.Run()).Why);
        Assert.Contains(En.Get("error.signed_out"), (await new Stubs((409, needsKey)) { Mint = (401, "") }.Run()).Why);
    }

    /// <summary>The iPad's testAKeptEnrolmentMintsNoConnectorKey: planque keeps the key it holds, and nothing is minted.</summary>
    [Fact]
    public async Task A_kept_enrolment_mints_no_key()
    {
        var stubs = new Stubs((200, """{"tenant": "t_01", "device": "ab12", "kept": true}"""));

        Assert.Equal(new EnrolResult(EnrolOutcome.Kept), (await stubs.Run()));
        Assert.Equal(0, stubs.Mints);
        Assert.Equal([new Dictionary<string, string> { ["device_key"] = "MFkB" }], stubs.Enrolments);
    }

    /// <summary>
    /// The iPad's testWithNoStandingKeyOneIsMintedAndHandedOver: planque says it needs a key (needs_key), and only then: one
    /// key minted and handed over, the first enrolment without it.
    /// </summary>
    [Fact]
    public async Task Only_when_planque_needs_a_key_is_one_minted_and_handed_over()
    {
        var stubs = new Stubs((409, """{"error": "no connector key stands for this account", "code": "needs_key"}"""), (201, """{"tenant": "t_01"}"""));

        Assert.Equal(new EnrolResult(EnrolOutcome.Enrolled), (await stubs.Run()));
        Assert.Equal(1, stubs.Mints);
        Assert.Equal([null, "kiff_live_connector"], stubs.Enrolments.Select(e => e.GetValueOrDefault("connector_key")));
        Assert.All(stubs.Enrolments, e => Assert.Equal("MFkB", e["device_key"]));
    }

    /// <summary>
    /// Anything else stops with why, and no key is minted, so none is left live in KIFF that no one can revoke: planque not
    /// reached, a 5xx, a replayed signature, a changed enrolment, a 2xx that didn't keep its key.
    /// </summary>
    [Theory]
    [InlineData(0, "", "It didn't reach")]
    [InlineData(503, """{"error": "planque is down"}""", "planque is down")]
    [InlineData(401, """{"error": "signature replayed", "code": "signature_replayed"}""", "signature replayed")]
    [InlineData(409, """{"error": "the enrolment changed", "code": "enrolment_changed"}""", "the enrolment changed")]
    [InlineData(400, """{"error": "KIFF did not accept the connector key"}""", "KIFF did not accept the connector key")]
    [InlineData(409, """{"error": "conflict"}""", "conflict")]
    [InlineData(201, """{"tenant": "t_01", "device": "ab12"}""", "201")]
    public async Task Anything_else_stops_and_mints_nothing(int status, string answer, string why)
    {
        var stubs = status == 0 ? new Stubs() { PlanqueUnreached = true } : new Stubs((status, answer));

        var result = await stubs.Run();

        Assert.Equal(EnrolOutcome.NotSetUp, result.Outcome);
        Assert.StartsWith("Connecting apps isn't set up.", result.Why);
        Assert.Contains(why, result.Why);
        Assert.Equal(0, stubs.Mints);
        Assert.Single(stubs.Enrolments);
    }

    /// <summary>As the iPad's enrolPlanque: KIFF's 403 is an admin's; planque's 409 is another device's; else why.</summary>
    [Fact]
    public async Task What_stops_it_says_why()
    {
        var needsKey = (409, """{"code": "needs_key"}""");
        Assert.Equal(new EnrolResult(EnrolOutcome.AdminOnly, "Only an admin of this KIFF account can connect apps."),
            (await new Stubs(needsKey) { Mint = (403, """{"error": "forbidden"}""") }.Run()));
        Assert.Equal(new EnrolResult(EnrolOutcome.OtherDevice, "Another device connects this account's apps for now. Connect them from that one."),
            (await new Stubs(needsKey, (409, """{"error": "enrolled"}""")).Run()));
        // A 409 with a code is that code's, never "another device".
        Assert.Equal(new EnrolResult(EnrolOutcome.NotSetUp, "Connecting apps isn't set up. signature replayed"),
            (await new Stubs(needsKey, (409, """{"error": "signature replayed", "code": "signature_replayed"}""")).Run()));
        Assert.Equal(new EnrolResult(EnrolOutcome.NotSetUp, "Connecting apps isn't set up. planque is down"),
            (await new Stubs(needsKey, (503, """{"error": "planque is down"}""")).Run()));
        Assert.Equal(new EnrolResult(EnrolOutcome.NotSetUp, "Connecting apps isn't set up. no keys today"),
            (await new Stubs(needsKey) { Mint = (500, """{"error": "no keys today"}""") }.Run()));
        Assert.Equal(EnrolOutcome.NotSetUp, (await new Stubs(needsKey) { Mint = (201, """{"id": "key_c"}""") }.Run()).Outcome);
        var offline = (await new Stubs(needsKey) { KiffUnreached = true }.Run());
        Assert.Equal((EnrolOutcome.NotSetUp, false), (offline.Outcome, offline.Ready));
    }

    /// <summary>The enrolment is signed for planque as this device, exactly as sent; the mint for KIFF, as a setup call.</summary>
    [Fact]
    public async Task Each_call_is_signed_as_this_device()
    {
        using var key = new TestSigner();
        var body = PlanqueEnrolment.Body(key.PublicKeyDer, "kiff_live_connector");
        var header = await PlanqueProof.SignAsync("t_01", null, () => Task.FromResult<string?>("dev_pc01"), key, () => "2026-11-15T16:00:00.000Z",
            "POST", PlanqueEnrolment.Path("t_01"), body);

        Assert.StartsWith("t=2026-11-15T16:00:00.000Z,v1=", header);
        Assert.EndsWith(",d=dev_pc01", header);
        var signature = Convert.FromBase64String(header!.Split(",v1=")[1].Split(",d=")[0]);
        Assert.True(TestSigner.Verify(key.PublicKeyDer, PlanqueProof.Message(PlanqueProof.Api, "t_01", "POST", "/v1/accounts/t_01/enrolment", body, "2026-11-15T16:00:00.000Z"), signature));
        Assert.Null(await PlanqueProof.SignAsync("t_01", null, () => Task.FromResult<string?>(null), key, () => "t", "POST", "/x", body));

        var at = "2026-11-15T16:00:00.456Z";
        var mint = await PlanqueEnrolment.MintSignatureAsync(key, "t_01", at);
        Assert.StartsWith("t=2026-11-15T16:00:00.456Z,v1=", mint);
        Assert.True(TestSigner.Verify(key.PublicKeyDer, KiffSigning.RequestV1("t_01", "POST", "/v1/keys", PlanqueEnrolment.MintBody(), at), Convert.FromBase64String(mint.Split(",v1=")[1])));
        Assert.Equal("""{"label":"planque","roles":["connector"]}""", Encoding.UTF8.GetString(PlanqueEnrolment.MintBody()));
        Assert.Equal("tafs.planque.v1|t_01", Encoding.UTF8.GetString(PlanqueEnrolment.GateMessage("t_01")));
    }

    static KeptSession Session(ApprovalMode mode, string? passkey = null) =>
        new KeptSession("api.kiff.dev", "token", "t_01", "s1", "dev_pc01", mode, DateTimeOffset.MaxValue, "k") { PasskeyId = passkey };

    /// <summary>
    /// Who may set planque up, before anything is sent: only a passkey session behind Windows Hello over
    /// tafs.planque.v1|{tenant}, with its own passkey; a view session, a key session (its device key asks no one), or a
    /// passkey session that doesn't know its passkey, never. Closing Windows Hello, or one that doesn't verify, stops quietly.
    /// </summary>
    [Fact]
    public async Task Who_may_set_planque_up()
    {
        var asked = new List<(byte[] Id, string Message)>();
        Task Hello(byte[] id, byte[] message) { asked.Add((id, Encoding.UTF8.GetString(message))); return Task.CompletedTask; }

        Assert.Null(await PlanqueEnrolment.GateAsync(En, Session(ApprovalMode.Passkey, KiffSigning.Base64Url([1, 2, 255])), Hello));
        Assert.Equal([1, 2, 255], asked[0].Id);
        Assert.Equal("tafs.planque.v1|t_01", asked[0].Message);

        var cant = new EnrolResult(EnrolOutcome.CantApprove, "This device can't approve. Approve on another device signed in to your account.");
        Assert.Equal(cant, await PlanqueEnrolment.GateAsync(En, Session(ApprovalMode.View), Hello));
        Assert.Equal(cant, await PlanqueEnrolment.GateAsync(En, Session(ApprovalMode.Key), Hello));
        Assert.Equal(cant, await PlanqueEnrolment.GateAsync(En, Session(ApprovalMode.Passkey), Hello));
        Assert.Single(asked);

        Assert.Equal(new EnrolResult(EnrolOutcome.Cancelled),
            await PlanqueEnrolment.GateAsync(En, Session(ApprovalMode.Passkey, "AQ"), (_, _) => throw new OperationCanceledException()));
        Assert.Equal(new EnrolResult(EnrolOutcome.Cancelled),
            await PlanqueEnrolment.GateAsync(En, Session(ApprovalMode.Passkey, "AQ"), (_, _) => throw new Tafs.Core.Approvals.PasskeyRefusedException("Windows Hello didn't verify the person")));
    }
}

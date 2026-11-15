using System.Net.Http;
using System.Text;
using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Kiff;

/// <summary>
/// Setting this device up with planque, as the iPad's setUpPlanque and enrolPlanque with iOS #1106: planque asked first
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

        public Task<EnrolResult> Run() => PlanqueEnrolment.EnrolAsync(En, DeviceKey, Post, MintKey);
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

        var at = new DateTimeOffset(2026, 11, 15, 16, 0, 0, TimeSpan.Zero);
        var mint = await PlanqueEnrolment.MintSignatureAsync(key, "t_01", at);
        Assert.StartsWith("t=2026-11-15T16:00:00Z,v1=", mint);
        Assert.True(TestSigner.Verify(key.PublicKeyDer, KiffSigning.RequestV1("t_01", "POST", "/v1/keys", PlanqueEnrolment.MintBody(), at), Convert.FromBase64String(mint.Split(",v1=")[1])));
        Assert.Equal("""{"label":"planque","roles":["connector"]}""", Encoding.UTF8.GetString(PlanqueEnrolment.MintBody()));
        Assert.Equal("tafs.planque.v1|t_01", Encoding.UTF8.GetString(PlanqueEnrolment.GateMessage("t_01")));
    }
}

using System.Text;
using Tafs.Core.Record;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Record;

/// <summary>A line's KIFF receipt, as the iPad's ReceiptView and RawJSON, against the iPad's receipt signed the way KIFF signs.</summary>
[Part("record")]
public class ReceiptTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    // The iPad's ReceiptCheckTests.signed: its chain has what a re-encoding would change (escaped <, > and &, and Go's 1e+21).
    const string Signed = """{"schema_version":"0.1","trace_id":"01KTRACE","tenant_id":"t_1","entity_id":"pi_3Q_test","entity_type":"Payment","action_name":"stripe_create_refund","outcome":"allowed","issued_at":"2026-10-21T00:41:03.12Z","causal_chain":[{"sequence":1,"kind":"decision","actor_id":"agt_refunds","occurred_at":"2026-10-21T00:41:03.12Z","message":"held: over \u003c€100\u003e \u0026 more","data_hash":"aa"},{"sequence":2,"kind":"settled","occurred_at":"2026-10-21T00:41:04.12Z","data_hash":"bb","data":{"amount":15000,"answer":"approved_once","big":1e+21,"tool":{"status":"done"}}}],"attestation":{"algorithm":"secp256k1+keccak256","signature":"79917d81e09b24ef196e5b71c72ea1c5cc8951e628b63922072ef59832d0a43e4a0b198bead4ddad1312fe7f1e9b6d41b5f617fe08c5aed59abcd9e5b225a2ce01","wallet_address":"0x2c7536e3605d9c16a7a3d7b1898e529396a65c23","anchor_tx_hash":"","anchored_at":"2026-10-21T00:41:03.12Z"},"risk_score":null,"compliance_map":null,"insurer_envelope":null}""";

    static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>As the iPad's testTheChainIsHashedAsWrittenNotReencoded: the chain's own bytes, escapes and all.</summary>
    [Fact]
    public void The_chain_is_taken_as_written()
    {
        var chain = Encoding.UTF8.GetString(RawJson.Value(Bytes(Signed), "causal_chain")!);

        Assert.StartsWith("""[{"sequence":1,"kind":"decision""", chain);
        Assert.Contains("""\u003c€100\u003e \u0026 more""", chain);
        Assert.EndsWith("\"big\":1e+21,\"tool\":{\"status\":\"done\"}}}]", chain);
        Assert.Null(RawJson.Value(Bytes(Signed), "missing"));
        Assert.Equal("\"0.1\"", Encoding.UTF8.GetString(RawJson.Value(Bytes(" {\n\"schema_version\" : \"0.1\" }"), "schema_version")!));
        Assert.Null(RawJson.Value(Bytes("""["causal_chain"]"""), "causal_chain"));
        Assert.Null(RawJson.Value(Bytes("""{"causal_chain": [1, 2"""), "causal_chain"));
    }

    /// <summary>What happened, as KIFF signed it: its message, else its kind in words; when; its data's hash.</summary>
    [Fact]
    public void Its_steps_are_KIFF_s()
    {
        var steps = Receipt.Steps(Bytes(Signed.Replace("\"data_hash\":\"bb\"", "\"data_hash\":\"0123456789abcdef\"")));

        Assert.Equal(["held: over <€100> & more", "settled"], steps.Select(s => s.Words));
        Assert.Equal(["aa", "0123…cdef"], steps.Select(s => s.Hash));
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 0, 41, 4, 120, TimeSpan.Zero), steps[1].At);
        Assert.Equal("approved_once", steps[1].Answer);
        Assert.Equal("20:41:04", Receipt.Shown(En, steps[1].At!.Value, TimeZoneInfo.CreateCustomTimeZone("x", TimeSpan.FromHours(-4), "x", "x"), withDate: false));
        Assert.Equal("2026-10-21T00:41:03.12Z", Receipt.Field(Bytes(Signed), "issued_at"));
        Assert.StartsWith("79917d81", Receipt.Field(Bytes(Signed), "signature", attestation: true));
        Assert.Empty(Receipt.Steps(Bytes("""{"causal_chain": {"a": 1}}""")));
        Assert.Empty(Receipt.Steps(Bytes("not json")));
        Assert.Equal("/v1/receipts/01K%2FTRACE", Receipt.Path("01K/TRACE"));
    }

    static RecordEntry Entry(EntryKind kind, string call = "", string? amount = null)
    {
        var json = $$"""{"calls": [{"proposal_id": "p", "action_name": "a", "outcome": "allowed", "created_at": "2026-11-15T09:00:00Z", "held": false{{call}}}]}""";
        var c = KiffActivity.Read(System.Text.Json.JsonDocument.Parse(json).RootElement)![0];
        return new RecordEntry("Refunds", 4, kind, "Refund a payment", "detail", c.CreatedAt, amount, c);
    }

    /// <summary>The sheet's head, Outcome, the receipt's decision and Decided by, as the iPad's stateLabel, outcome, decisionWords and decidedBy.</summary>
    [Theory]
    [InlineData(EntryKind.Done, "", "€150", "Done within the Card", "Done. €150 moved.", "allowed by the Card", "KIFF, against the Card")]
    [InlineData(EntryKind.Approved, """, "held": true""", null, "Approved once by you", "Done.", "held → approved by you", "You, approving once")]
    [InlineData(EntryKind.Approved, """, "held": true, "answer": {"decision": "approved", "by": "admin", "signed": true, "device": "iPad"}""", null,
        "Approved once by an admin", "Done.", "held → approved once by an admin", "An admin, signed on iPad")]
    [InlineData(EntryKind.Approved, """, "held": true, "answer": {"decision": "approved", "you": true, "signed": true}""", null,
        "Approved once by you", "Done.", "held → approved once by you", "You, signed on your device")]
    [InlineData(EntryKind.Refused, """, "outcome": "denied", "answer": {"decision": "refused", "you": true}""", null,
        "Refused by you", "Not sent. Refused by you.", "held → refused by you", "You")]
    [InlineData(EntryKind.Refused, """, "outcome": "denied" """, null, "Refused", "Not sent. Its Card refused it.", "refused by the Card", "KIFF, against the Card")]
    [InlineData(EntryKind.Held, """, "outcome": "owner_required" """, null, "Held for you", "Waiting for your answer.", "held for you by the Card", "KIFF, against the Card")]
    [InlineData(EntryKind.Sending, "", null, "In progress", "detail", "allowed by the Card", "KIFF, against the Card")]
    public void The_sheet_s_facts(EntryKind kind, string call, string? amount, string state, string outcome, string decision, string decidedBy)
    {
        var e = Entry(kind, call, amount);
        Assert.Equal((state, outcome, decision, decidedBy), (Receipt.StateLabel(En, e), Receipt.Outcome(En, e), Receipt.DecisionWords(En, e), Receipt.DecidedBy(En, e)));
    }

    /// <summary>A sample line, with no KIFF call, was decided by its Card.</summary>
    [Fact]
    public void A_sample_line_was_decided_by_its_Card() =>
        Assert.Equal("The Card", Receipt.DecidedBy(En, new RecordEntry("Refunds", 4, EntryKind.Done, "t", "d", DateTimeOffset.UnixEpoch)));

    /// <summary>As the iPad's amountMismatch and answerMismatch: the line against what KIFF signed; KIFF records a refusal as "reject".</summary>
    [Theory]
    [InlineData(""", "amount": {"value": "150.00", "unit": "EUR"}""", """{"value": "150.00", "unit": "EUR"}""", "approved_once", null)]
    [InlineData(""", "amount": {"value": "150.00", "unit": "EUR"}""", """{"value": "950.00", "unit": "EUR"}""", "approved_once", "The amount on this line doesn't match KIFF's receipt.")]
    [InlineData(""", "amount": {"value": "150.00", "unit": "EUR"}""", """{"value": "150.00"}""", "approved_once", "The amount on this line doesn't match KIFF's receipt.")]
    [InlineData(""", "answer": {"decision": "approved"}""", "null", "approved_once", null)]
    [InlineData(""", "answer": {"decision": "approved"}""", "null", "reject", "The answer on this line doesn't match KIFF's receipt.")]
    [InlineData(""", "answer": {"decision": "refused"}""", "null", "approved_once", "The answer on this line doesn't match KIFF's receipt.")]
    [InlineData(""", "answer": {"decision": "refused"}""", "null", "reject", null)]
    public void The_line_must_be_what_KIFF_signed(string call, string signedAmount, string settled, string? why)
    {
        var raw = Bytes($$$"""{"causal_chain": [{"kind": "decision", "data": {"amount": {{{signedAmount}}}}}, {"kind": "hold_settled", "data": {"answer": "{{{settled}}}"}}]}""");
        Assert.Equal(why, Receipt.Mismatch(En, Entry(EntryKind.Approved, call), Receipt.Steps(raw)));
    }
}

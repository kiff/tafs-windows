using System.Text;
using System.Text.RegularExpressions;
using Tafs.Core.Record;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Record;

/// <summary>
/// This PC's check of a KIFF receipt, as iOS's ReceiptCheckTests, against the same receipt signed the way KIFF
/// signs and written by the receipts endpoint's encoder, with a test key. Its chain has what a re-encoding would change:
/// escaped &lt;, &gt; and &amp;, a map's sorted keys, and Go's 1e+21.
/// </summary>
[Part("record")]
public class ReceiptCheckTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    const string Signed = """{"schema_version":"0.1","trace_id":"01KTRACE","tenant_id":"t_1","entity_id":"pi_3Q_test","entity_type":"Payment","action_name":"stripe_create_refund","outcome":"allowed","issued_at":"2026-10-21T00:41:03.12Z","causal_chain":[{"sequence":1,"kind":"decision","actor_id":"agt_refunds","occurred_at":"2026-10-21T00:41:03.12Z","message":"held: over \u003c€100\u003e \u0026 more","data_hash":"aa"},{"sequence":2,"kind":"settled","occurred_at":"2026-10-21T00:41:04.12Z","data_hash":"bb","data":{"amount":15000,"answer":"approved_once","big":1e+21,"tool":{"status":"done"}}}],"attestation":{"algorithm":"secp256k1+keccak256","signature":"79917d81e09b24ef196e5b71c72ea1c5cc8951e628b63922072ef59832d0a43e4a0b198bead4ddad1312fe7f1e9b6d41b5f617fe08c5aed59abcd9e5b225a2ce01","wallet_address":"0x2c7536e3605d9c16a7a3d7b1898e529396a65c23","anchor_tx_hash":"","anchored_at":"2026-10-21T00:41:03.12Z"},"risk_score":null,"compliance_map":null,"insurer_envelope":null}""";
    const string TestSigner = "0x2c7536e3605d9c16a7a3d7b1898e529396a65c23";

    static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
    static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    /// <summary>JSON written with single quotes, for the fragments replaced in the receipt.</summary>
    static string Q(string s) => s.Replace('\'', '"');

    static byte[] Repeat(byte b, int count) => Enumerable.Repeat(b, count).ToArray();

    [Fact]
    public void Keccak_matches_the_standard_vectors()
    {
        Assert.Equal("c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470", Hex(ReceiptCheck.Keccak256([])));
        Assert.Equal("4e03657aea45a94fc7d47ba826c8d667c0d1e6e33a64a036ec44f58fa12d6c45", Hex(ReceiptCheck.Keccak256(B("abc"))));
        // Either side of a block (136 bytes), against Go's sha3.NewLegacyKeccak256.
        Assert.Equal("34367dc248bbd832f4e3e69dfaac2f92638bd0bbd18f2912ba4ef454919cf446", Hex(ReceiptCheck.Keccak256(Repeat(0x61, 135))));
        Assert.Equal("a6c4d403279fe3e0af03729caada8374b5ca54d8065329a3ebcaeb4b60aa386e", Hex(ReceiptCheck.Keccak256(Repeat(0x61, 136))));
        Assert.Equal("5b7e0e47a96f32a88b4f14ca177982790807c40e1a105742ba0fc1babe1ef826", Hex(ReceiptCheck.Keccak256(Repeat(0x61, 300))));
    }

    [Fact]
    public void A_receipt_KIFF_signed_checks_out() =>
        Assert.Equal(new ReceiptCheck.Verified(TestSigner), ReceiptCheck.Check(En, B(Signed), TestSigner));

    [Fact]
    public void The_chain_is_hashed_as_written_not_reencoded()
    {
        var chain = Encoding.UTF8.GetString(RawJson.Value(B(Signed), "causal_chain")!);
        Assert.StartsWith(Q("[{'sequence':1,'kind':'decision'"), chain);
        // As Go wrote it, escaped: a decoder and re-encoder would write "<€100> & more".
        Assert.Contains(@"\u003c€100\u003e \u0026 more", chain);
        Assert.EndsWith(Q("'big':1e+21,'tool':{'status':'done'}}}]"), chain);
        Assert.Null(RawJson.Value(B(Signed), "missing"));
    }

    /// <summary>A receipt with a key written twice isn't one KIFF wrote: refused, at its top and in its attestation.</summary>
    [Fact]
    public void A_receipt_that_repeats_a_key_is_refused()
    {
        Assert.Equal(["a", "b", "c"], RawJson.Keys(B(Q("{'a':1,'b':{'a':2},'c':[{'a':3}]}"))));
        Assert.True(RawJson.KeysAreUnique(B(Signed)));
        var twice = Signed[..^1] + Q(",'causal_chain':[{'sequence':1,'kind':'decision','message':'refund €5'}]}");
        Assert.Equal(new ReceiptCheck.Failed("The receipt couldn't be read."), ReceiptCheck.Check(En, B(twice), TestSigner));
        var signer = Signed.Replace(Q("'algorithm':'secp256k1+keccak256',"), Q("'algorithm':'secp256k1+keccak256','wallet_address':'0x0',"));
        Assert.NotEqual(Signed, signer);
        Assert.Equal(new ReceiptCheck.Failed("The receipt couldn't be read."), ReceiptCheck.Check(En, B(signer), TestSigner));
        // One copy each still checks out.
        Assert.Equal(new ReceiptCheck.Verified(TestSigner), ReceiptCheck.Check(En, B(Signed), TestSigner));
    }

    /// <summary>"What happened" is drawn only for a receipt this PC checked as KIFF's, and only when it matches the line.</summary>
    [Fact]
    public void What_happened_is_drawn_only_for_a_checked_receipt_that_matches_the_line()
    {
        Assert.True(Receipt.ShowsWhatHappened(new ReceiptCheck.Verified(TestSigner), mismatch: false));
        Assert.False(Receipt.ShowsWhatHappened(new ReceiptCheck.Verified(TestSigner), mismatch: true));
        Assert.False(Receipt.ShowsWhatHappened(new ReceiptCheck.Failed("It isn't signed by KIFF."), mismatch: false));
        Assert.False(Receipt.ShowsWhatHappened(new ReceiptCheck.NotSignedYet(), mismatch: false));
        Assert.False(Receipt.ShowsWhatHappened(null, mismatch: false));
    }

    [Fact]
    public void A_changed_byte_in_the_chain_fails()
    {
        var changed = Signed.Replace(Q("'data_hash':'bb'"), Q("'data_hash':'bc'"));
        Assert.NotEqual(Signed, changed);
        Assert.Equal(new ReceiptCheck.Failed("The signature doesn't match these steps."), ReceiptCheck.Check(En, B(changed), TestSigner));
        var amount = Signed.Replace(Q("'amount':15000"), Q("'amount':95000"));
        Assert.Equal(new ReceiptCheck.Failed("The signature doesn't match these steps."), ReceiptCheck.Check(En, B(amount), TestSigner));
    }

    /// <summary>The test key's receipt is sound, but production pins KIFF's signer.</summary>
    [Fact]
    public void A_receipt_signed_by_another_key_is_not_KIFFs() =>
        Assert.Equal(new ReceiptCheck.Failed("It isn't signed by KIFF."), ReceiptCheck.Check(En, B(Signed)));

    [Fact]
    public void Not_signed_yet_is_not_a_failure()
    {
        var pending = Regex.Replace(Signed, Q(@"'attestation':\{[^}]*\}"), Q("'attestation':null"));
        Assert.NotEqual(Signed, pending);
        Assert.Equal(new ReceiptCheck.NotSignedYet(), ReceiptCheck.Check(En, B(pending)));
    }

    [Fact]
    public void A_malformed_signature_fails_without_stopping_the_app()
    {
        Assert.Null(ReceiptCheck.RecoverAddress(Repeat(1, 32), new byte[65]));
        Assert.Null(ReceiptCheck.RecoverAddress(Repeat(1, 32), Repeat(0xFF, 65)));
        var shortOne = Regex.Replace(Signed, Q("'signature':'[0-9a-f]+'"), Q("'signature':'00'"));
        Assert.Equal(new ReceiptCheck.Failed("The signature is malformed."), ReceiptCheck.Check(En, B(shortOne), TestSigner));
        Assert.Equal(new ReceiptCheck.Failed("The receipt couldn't be read."), ReceiptCheck.Check(En, B("not json")));
        Assert.Equal(new ReceiptCheck.Failed("It's signed in a way this app doesn't know."),
            ReceiptCheck.Check(En, B(Signed.Replace("secp256k1+keccak256", "ed25519")), TestSigner));
    }

    /// <summary>
    /// A recovery id outside 0–3, after 27 is taken off a v of 27 or more, is refused. v = 4, 26 and 31 are the edges either
    /// side of 0–3 and 27–30; 28 (27 + 1) is the same signature, written the Ethereum way, and still checks out.
    /// </summary>
    [Fact]
    public void A_recovery_id_outside_zero_to_three_is_refused()
    {
        string WithV(string v) => Regex.Replace(Signed, Q("'signature':'([0-9a-f]{128})01'"), m => Q("'signature':'") + m.Groups[1].Value + v + "\"");
        Assert.NotEqual(Signed, WithV("1c"));
        foreach (var v in new[] { "04", "1a", "1f" })
            Assert.Equal(new ReceiptCheck.Failed("The signature doesn't match these steps."), ReceiptCheck.Check(En, B(WithV(v)), TestSigner));
        Assert.Equal(new ReceiptCheck.Verified(TestSigner), ReceiptCheck.Check(En, B(WithV("1c")), TestSigner));
        foreach (var v in new byte[] { 4, 26, 31 })
        {
            var raw = Repeat(1, 65);
            raw[64] = v;
            Assert.Null(ReceiptCheck.RecoverAddress(Repeat(1, 32), raw));
        }
    }
}

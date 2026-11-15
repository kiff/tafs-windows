using System.Text;
using Tafs.Core.Kiff;

namespace Tafs.Core.Tests.Kiff;

/// <summary>planque's grants and their digests, as the iPad's PlanqueClient.grants, against the iPad's vectors, made with planque's Go code.</summary>
[Part("approvals")]
public class PlanqueGrantsTests
{
    static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    const string V2Request = """{"v":"planque.grant.v1","grant":"g_02","tenant":"t_01","server":"https://mcp.qonto.com/mcp","agent":"books","tools":[{"tool":"list_invoices","read_only":true}],"asked_by":"setup-agent","created_at":"2026-11-11T16:00:00Z"}""";
    const string V2Text = "books pourra lire vos factures sur mcp.qonto.com";
    const string V2Digest = "f1ca3ab371ecf97a82d134204e4931ef39ea474b7cd6a3e233b36f97499fbb32";

    static string Grant(string id = "g_01", string digest = JcsTests.Digest, string more = "") =>
        $$$"""{"id":"{{{id}}}","status":"pending",{{{more}}}"text":"refunds may refund payments at mcp.stripe.com","digest":"{{{digest}}}","request":{{{JcsTests.Request}}},"audience":"https://planque.kiff.dev/mcp/t_01/{{{id}}}","vendor":"Stripe","events":[{"at":"2026-10-17T10:00:00.123456789Z","what":"asked by setup-agent"}]}""";

    static string V2Grant(string digest = V2Digest, string? lang = "fr", string text = V2Text, string version = "2") =>
        $$$"""{"id":"g_02","status":"pending","text":"{{{text}}}",{{{(lang is null ? "" : $"\"lang\":\"{lang}\",")}}}"digest_v":{{{version}}},"digest":"{{{digest}}}","request":{{{V2Request}}},"vendor":"Qonto","events":[]}""";

    static IReadOnlyList<PlanqueGrant> Listed(params string[] grants) => PlanqueGrants.Read(Bytes($"{{\"grants\":[{string.Join(',', grants)}]}}"))!;

    /// <summary>The iPad's testEveryCallIsSignedAndTheDigestIsChecked: only a grant whose digest is its request's matches.</summary>
    [Fact]
    public void Each_grant_s_digest_is_checked()
    {
        var grants = Listed(Grant(), Grant("g_02", new string('0', 64)));

        Assert.Equal([true, false], grants.Select(g => g.DigestMatches));
        var g = grants[0];
        Assert.Equal(("Stripe", "asked by setup-agent", "refunds"), (g.Vendor, g.Events[0].What, g.Request.Agent));
        Assert.Equal(["refund_payment", "list_payments"], g.Request.Tools.Select(t => t.Action));
        Assert.Equal([null, true], g.Request.Tools.Select(t => t.ReadOnly));
    }

    /// <summary>
    /// The iPad's v2 tests: with digest_v 2 the digest covers the words and their language too; a changed word, another
    /// language, an unknown version or a v1 digest under the v2 marker never matches, and a v1 grant beside it still does.
    /// </summary>
    [Fact]
    public void A_v2_digest_covers_the_words_and_their_language()
    {
        var covered = PlanqueGrants.DigestCovers(Jcs.Parse(Bytes(V2Grant())))!;
        Assert.Equal("""{"lang":"fr","request":{"agent":"books","asked_by":"setup-agent","created_at":"2026-11-11T16:00:00Z","grant":"g_02","server":"https://mcp.qonto.com/mcp","tenant":"t_01","tools":[{"read_only":true,"tool":"list_invoices"}],"v":"planque.grant.v1"},"text":"books pourra lire vos factures sur mcp.qonto.com"}""",
            Encoding.UTF8.GetString(Jcs.Canonical(covered)));
        Assert.Equal(Jcs.Parse(Bytes(JcsTests.Request)), PlanqueGrants.DigestCovers(Jcs.Parse(Bytes($$"""{"id":"g_01","request":{{JcsTests.Request}}}"""))));
        Assert.Null(PlanqueGrants.DigestCovers(Jcs.Parse(Bytes(V2Grant(version: "3")))));
        Assert.Null(PlanqueGrants.DigestCovers(Jcs.Parse(Bytes(V2Grant(lang: null)))));

        Assert.Equal([true, true], Listed(Grant(), V2Grant()).Select(g => g.DigestMatches));
        Assert.Equal([true, false], Listed(Grant(), V2Grant(text: "books pourra lire et payer vos factures sur mcp.qonto.com")).Select(g => g.DigestMatches));
        Assert.Equal([true, false], Listed(Grant(), V2Grant(lang: "en")).Select(g => g.DigestMatches));
        Assert.Equal([true, false], Listed(Grant(), V2Grant(digest: "adcf390e1971e767d02d3ca528a4156047bf6de4648b720f91b75b07bd6050b6")).Select(g => g.DigestMatches));
    }

    /// <summary>As the iPad's decoder: a grant it can't read fails the read; health and actions are read leniently.</summary>
    [Fact]
    public void A_grant_is_read_as_the_iPad_reads_it()
    {
        var g = Listed(Grant(more: """ "signed_in":true,"held_until":"2026-10-18T17:05:00.123456789Z","terms":["a (b)"],"health":{"state":"working","ok":true,"words":" Works "},"actions":[{"id":"remove","words":"Remove","how":"delete"},7],"profile_ignored":1, """))[0];
        Assert.True(g.SignedIn);
        Assert.Equal("2026-10-18T17:05:00.123456789Z", g.HeldUntil);
        Assert.Equal(["a (b)"], g.Terms!);
        Assert.Equal(new GrantHealth("working", true, "Works"), g.Health);
        Assert.Equal([new GrantAction("remove", "Remove", "delete", null), new GrantAction("", "", "", null)], g.Actions!);
        Assert.Equal(new GrantHealth("", false, ""), Listed(Grant(more: "\"health\":\"x\","))[0].Health);

        Assert.Null(PlanqueGrants.Read(Bytes($$"""{"grants":[{{Grant()}},{"id":"g_02","status":"pending"}]}""")));
        Assert.Null(PlanqueGrants.Read(Bytes($$"""{"grants":[{{Grant(more: "\"actions\":{},")}}]}""")));
        Assert.Null(PlanqueGrants.Read(Bytes($$"""{"grants":[{{Grant(more: "\"signed_in\":\"yes\",")}}]}""")));
        Assert.Null(PlanqueGrants.Read(Bytes("""{"grants":[{"id":"g","status":"s","text":"t","digest":"d","request":{"server":"s","agent":"a","tools":[{"name":"n"}]},"events":[]}]}""")));
        Assert.Null(PlanqueGrants.Read(Bytes("not json")));
        Assert.Empty(PlanqueGrants.Read(Bytes("""{"grants":[]}"""))!);
    }

    /// <summary>planque's calls go to planque's exact host only, and KIFF's token never goes there.</summary>
    [Theory]
    [InlineData("https://planque.kiff.dev/v1/accounts/t/grants", true)]
    [InlineData("https://PLANQUE.kiff.dev/v1", true)]
    [InlineData("http://planque.kiff.dev/v1", false)]
    [InlineData("https://planque.kiff.dev:8443/v1", false)]
    [InlineData("https://x@planque.kiff.dev/v1", false)]
    [InlineData("https://evil.planque.kiff.dev/v1", false)]
    [InlineData("https://api.kiff.dev/v1", false)]
    public void Planque_s_calls_go_to_planque_only(string url, bool planque)
    {
        Assert.Equal(planque, PlanqueProof.IsPlanque(new Uri(url)));
        Assert.False(KeptSessions.MaySendTo(new Uri("https://planque.kiff.dev/v1"), new Uri("https://api.kiff.dev")));
    }
}

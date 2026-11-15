using System.Security.Cryptography;
using System.Text;
using Tafs.Core.Kiff;

namespace Tafs.Core.Tests.Kiff;

/// <summary>
/// RFC 8785 as planque computes a grant's digest, as the iPad's JCS: planque's own jcs tests, and the iPad's grant vector,
/// which was made with planque's Go code.
/// </summary>
[Part("approvals")]
public class JcsTests
{
    static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);
    static string Canonical(string json) => Encoding.UTF8.GetString(Jcs.Canonical(Bytes(json)));

    /// <summary>planque's TestCanonical: RFC 8785 §3.2.2, keys by UTF-16 code units, nesting.</summary>
    [Theory]
    [InlineData("""{"numbers":[333333333.33333329,1E30,4.50,2e-3,0.000000000000000000000000001],"string":"\u20ac$\u000F\u000aA'\u0042\u0022\u005c\\\"\/","literals":[null,true,false]}""",
        """{"literals":[null,true,false],"numbers":[333333333.3333333,1e+30,4.5,0.002,1e-27],"string":"€$\u000f\nA'B\"\\\\\"/"}""")]
    [InlineData("""{"\ud83d\ude00":1,"\ufb33":2,"a":3}""", """{"a":3,"😀":1,"דּ":2}""")]
    [InlineData("""{"b":{"z":1,"a":[1,2]},"a":"x"}""", """{"a":"x","b":{"a":[1,2],"z":1}}""")]
    public void Canonical_is_planque_s(string json, string canonical) => Assert.Equal(canonical, Canonical(json));

    /// <summary>planque's TestNumber and the iPad's: ECMAScript's Number::toString.</summary>
    [Theory]
    [InlineData(0d, "0")]
    [InlineData(1d, "1")]
    [InlineData(-1d, "-1")]
    [InlineData(100d, "100")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1e20, "100000000000000000000")]
    [InlineData(0.000001, "0.000001")]
    [InlineData(0.0000001, "1e-7")]
    [InlineData(123.456, "123.456")]
    [InlineData(9007199254740991d, "9007199254740991")]
    [InlineData(5e-324, "5e-324")]
    [InlineData(double.MaxValue, "1.7976931348623157e+308")]
    [InlineData(1.5, "1.5")]
    public void Numbers_print_as_ECMAScript(double d, string text) => Assert.Equal(text, Jcs.EcmaScript(d));

    /// <summary>planque's TestRefusesUnsafeIntegers: 2^53 and 2^53 + 1 would hash the same.</summary>
    [Theory]
    [InlineData("""{"id":9007199254740992}""")]
    [InlineData("""{"id":9007199254740993}""")]
    [InlineData("""{"a":[{"id":-9007199254740993}]}""")]
    [InlineData("""{"a":1,}""")]
    [InlineData("""{"a":"\udc00"}""")]
    [InlineData("""{"a":01x}""")]
    public void What_planque_refuses_is_refused(string json)
    {
        Assert.Throws<Jcs.Failure>(() => Jcs.Canonical(Bytes(json)));
        Assert.Equal("""{"f":1e+300,"id":9007199254740991}""", Canonical("""{"id":9007199254740991,"f":1e300}"""));
    }

    // The iPad's PlanqueTests.request and its digest as planque computes it.
    internal const string Request = """{"v":"planque.grant.v1","grant":"g_01","tenant":"t_01","server":"https://mcp.stripe.com","sign_in_at":"https://access.stripe.com","agent":"refunds","tools":[{"tool":"create_refund","name":"refund_payment","amount_argument":"amount","amount_unit":"EUR","amount_scale":2,"fixed_arguments":{"reason":"requested_by_customer","metadata.n":1.50,"big":1e30}},{"tool":"list_payments","read_only":true}],"asked_by":"setup-agent","created_at":"2026-10-17T10:00:00Z","note":"é😀\u000f"}""";
    internal const string Digest = "6bc63ba1c1235801b7ddcb66a2e66947faefdd819e0ac0bb669062a063c048a3";
    /// <summary>The iPad's testDigestIsPlanques: the request's canonical form, and its SHA-256 as planque's.</summary>
    [Fact]
    public void The_digest_is_planque_s()
    {
        // -0 prints 0, as ECMAScript (beside 0, xUnit takes it for the same row).
        Assert.Equal("0", Jcs.EcmaScript(-0.0));
        var canonical = Jcs.Canonical(Bytes(Request));
        Assert.Equal("""{"agent":"refunds","asked_by":"setup-agent","created_at":"2026-10-17T10:00:00Z","grant":"g_01","note":"é😀\u000f","server":"https://mcp.stripe.com","sign_in_at":"https://access.stripe.com","tenant":"t_01","tools":[{"amount_argument":"amount","amount_scale":2,"amount_unit":"EUR","fixed_arguments":{"big":1e+30,"metadata.n":1.5,"reason":"requested_by_customer"},"name":"refund_payment","tool":"create_refund"},{"read_only":true,"tool":"list_payments"}],"v":"planque.grant.v1"}""",
            Encoding.UTF8.GetString(canonical));
        Assert.Equal(Digest, Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant());
    }
}

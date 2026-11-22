using System.Text.Json;
using Tafs.Core.SignIn;

namespace Tafs.Core.Tests.SignIn;

[Part("pairing")]
public class AppSignInTests
{
    [Fact]
    public void The_challenge_is_RFC_7636_s()
    {
        // RFC 7636, Appendix B.
        var a = AppSignIn.Attempt.With("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk", "s");
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", a.Challenge);
        var fresh = AppSignIn.Attempt.New();
        Assert.Equal(43, fresh.Verifier.Length);
        Assert.Equal(43, fresh.State.Length);
        Assert.NotEqual(fresh.State, AppSignIn.Attempt.New().State);
        Assert.Equal("Attempt(…)", fresh.ToString());
    }

    static Dictionary<string, string> Query(Uri link) =>
        link.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));

    [Fact]
    public void The_authorize_link_is_KIFF_s()
    {
        var a = AppSignIn.Attempt.With("v", "st8");
        var link = a.AuthorizeLink("pt-BR", "r@acme.test");
        Assert.Equal("https", link.Scheme);
        Assert.Equal("api.kiff.dev", link.Host);
        Assert.Equal("/oauth/authorize", link.AbsolutePath);
        var q = Query(link);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("tafs-app", q["client_id"]);
        Assert.Equal("dev.kiff.tafs:/oauth", q["redirect_uri"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal(a.Challenge, q["code_challenge"]);
        Assert.Equal("st8", q["state"]);
        Assert.Equal("pt", q["ui_locales"]);
        Assert.Equal("r@acme.test", q["login_hint"]);
        Assert.False(q.ContainsKey("scope"));
        // A language KIFF's page doesn't speak is left out: the page falls back to English.
        Assert.False(Query(a.AuthorizeLink("ja")).ContainsKey("ui_locales"));
    }

    [Fact]
    public void Only_this_attempt_s_link_back_is_taken()
    {
        var a = AppSignIn.Attempt.With("v", "st8");
        Assert.Equal(new AppSignIn.Back.Code("c1"), a.Answer("dev.kiff.tafs:/oauth?code=c1&state=st8"));
        Assert.Equal(new AppSignIn.Back.Refused("access_denied"), a.Answer("dev.kiff.tafs:/oauth?error=access_denied&state=st8"));
        // A refusal with escaped words comes through too.
        Assert.Equal(new AppSignIn.Back.Refused("access_denied"),
            a.Answer("dev.kiff.tafs:/oauth?error=access_denied&error_description=The%20person%20declined&state=st8"));
        foreach (var other in new[]
        {
            "dev.kiff.tafs:/oauth?code=c1&state=other",
            "dev.kiff.tafs:/oauth?code=c1",
            "dev.kiff.tafs:/oauth?code=c1&state=st8&state=st8",
            "dev.kiff.tafs:/oauth?code=c1&code=c2&state=st8",
            "dev.kiff.tafs:/other?code=c1&state=st8",
            "dev.kiff.tafs://oauth?code=c1&state=st8",
            "evil.tafs:/oauth?code=c1&state=st8",
            "https://kiff.dev/oauth?code=c1&state=st8",
            "dev.kiff.tafs:/oauth?code=&state=st8",
            "dev.kiff.tafs:/oauth?code=c1&error=x&state=st8",
            "dev.kiff.tafs:/oauth?code=c1&state=st8#x",
            "not a link",
        })
            Assert.Null(a.Answer(other));
    }

    [Fact]
    public void The_token_body_carries_the_terms_and_the_device_and_no_device_field_stands_in_for_an_OAuth_one()
    {
        var a = AppSignIn.Attempt.With("ver", "s");
        var device = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
        {
            ["public_key"] = "SPKI", ["device_name"] = "tafs on this Windows PC", ["platform"] = "windows", ["code"] = "not-this",
        });
        using var doc = JsonDocument.Parse(a.TokenBody("c1", "2026-11-14", device));
        var b = doc.RootElement;
        Assert.Equal("tafs-app", b.GetProperty("client_id").GetString());
        Assert.Equal("c1", b.GetProperty("code").GetString());
        Assert.Equal("ver", b.GetProperty("code_verifier").GetString());
        Assert.Equal("dev.kiff.tafs:/oauth", b.GetProperty("redirect_uri").GetString());
        Assert.Equal("2026-11-14", b.GetProperty("terms_version").GetString());
        Assert.Equal("SPKI", b.GetProperty("public_key").GetString());
        Assert.Equal("windows", b.GetProperty("platform").GetString());
    }

    [Fact]
    public void KIFF_s_answers_end_Continue_as_the_iPad_s()
    {
        var ok = AppSignIn.ReadToken(200,
            """{"access_token":"tok","token_type":"Bearer","expires_in":0,"tenant":"t","subject":"s","approval_mode":"view"}""");
        Assert.Equal("tok", ok.Session!.AccessToken);
        Assert.Equal(AppSignIn.Outcome.TermsChanged, AppSignIn.ReadToken(409, """{"error":"terms_outdated"}""").Outcome);
        var off = AppSignIn.ReadToken(400, """{"error":"unsupported_client","words":"Not on yet here."}""");
        Assert.Equal((AppSignIn.Outcome.NotOn, "Not on yet here."), (off.Outcome, off.Words));
        Assert.Equal(AppSignIn.Outcome.Failed, AppSignIn.ReadToken(400, """{"error":"invalid_grant"}""").Outcome);
        Assert.Null(AppSignIn.ReadToken(200, "not json").Session);
        Assert.Equal(AppSignIn.Outcome.Declined, AppSignIn.BackRefused("access_denied"));
        Assert.Equal(AppSignIn.Outcome.NotOn, AppSignIn.BackRefused("unsupported_client"));
        Assert.Equal(AppSignIn.Outcome.Failed, AppSignIn.BackRefused("temporarily_unavailable"));
    }

    [Fact]
    public void The_terms_version_in_force()
    {
        Assert.Equal("2026-11-14", AppSignIn.ReadTerms(200, """{"terms_version":"2026-11-14"}"""));
        Assert.Null(AppSignIn.ReadTerms(200, """{"terms_version":""}"""));
        Assert.Null(AppSignIn.ReadTerms(500, """{"terms_version":"x"}"""));
        Assert.Null(AppSignIn.ReadTerms(200, "nope"));
    }

    [Fact]
    public void A_code_refused_for_changed_terms_is_kept_50_seconds_only()
    {
        var at = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        var kept = new AppSignIn.Kept(AppSignIn.Attempt.With("v", "s"), "c1", at);
        Assert.True(AppSignIn.StillGood(kept, at.AddSeconds(49)));
        Assert.False(AppSignIn.StillGood(kept, at.AddSeconds(50)));
        Assert.False(AppSignIn.StillGood(null, at));
        Assert.Equal("Kept(…)", kept.ToString());
    }
}

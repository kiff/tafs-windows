using System.Text;
using System.Text.Json;
using Tafs.Core.Account;
using ContractFile = Tafs.Core.Contracts.ContractFile;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Account;

/// <summary>Account's AI-data consent and Coach's "Show suggestions", as tafs cloud sends them.</summary>
[Part("account")]
public class AccountSettingsTests
{
    static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void The_consent_reads_with_its_words_and_allows_its_version()
    {
        var c = AiConsent.Read(J("""{"consent":{"given":false,"version":"2026-11","words":{"title":"Your data and the AI","lines":["Coach reads your chats."," ",3],"allow":"Allow","not_now":"Not now","withdraw":"Withdraw","given":"AI use allowed"}}}"""))!;
        Assert.Equal((false, "2026-11", "Your data and the AI", "Allow"), (c.Given, c.Version, c.Words.Title, c.Words.Allow));
        Assert.Equal(["Coach reads your chats."], c.Words.Lines);
        Assert.True(c.Drawable);
        Assert.Equal("""{"version":"2026-11"}""", Encoding.UTF8.GetString(c.AllowBody()));

        var given = AiConsent.Read(J("""{"consent":{"given":true,"version":"2026-11","words":{"given":"AI use allowed","withdraw":"Withdraw"}}}"""))!;
        Assert.True(given is { Given: true, Drawable: true });
        // Nothing to draw without its words or version; another shape is none.
        Assert.False(AiConsent.Read(J("""{"consent":{"given":true,"version":"v"}}"""))!.Drawable);
        Assert.False(AiConsent.Read(J("""{"consent":{"given":false,"words":{"title":"t","allow":"a"}}}"""))!.Drawable);
        Assert.Null(AiConsent.Read(J("""{"consent":"yes"}""")));
    }

    /// <summary>
    /// As the iPad's <c>TafsCloud.refusal</c>: a turn refused for want of the consent carries its screen and says so once, for
    /// the window to put it up; an older screen's refused yes carries the new words, with no screen of its own.
    /// </summary>
    [Fact]
    public void A_turn_refused_for_the_consent_asks_for_it_once()
    {
        var en = WordBook.Load(SharedFiles.Strings, "en");
        var cases = ContractFile.Cases(SharedFiles.Contract("ai-consent.json"));
        var asked = new List<AiConsent>();
        void Ask(AiConsent c) => asked.Add(c);
        CloudRead.ConsentRequired += Ask;
        try
        {
            var turn = cases.First(c => c.Status == 403);
            var refused = CloudRead.Refused(en, turn.Status, turn.Body.GetRawText());
            Assert.True(refused.AsksConsent);
            Assert.Equal(("1", "Your data and the AI"), (refused.Consent!.Version, refused.Consent.Words.Title));
            Assert.Equal("Allow AI use first: Coach and your tafs need it to answer.", refused.Why);
            Assert.Equal(new Refusal(refused.Why!, null, Consent: true, Status: 403), Refusal.Of(refused, "x"));
            Assert.Equal("1", Assert.Single(asked, c => c == refused.Consent).Version);

            var older = cases.First(c => c.Status == 409);
            var newer = CloudRead.Refused(en, older.Status, older.Body.GetRawText());
            Assert.Equal("1", newer.Consent?.Version);
            Assert.False(newer.AsksConsent);
            Assert.DoesNotContain(newer.Consent, asked);

            // A consent it can't draw, or none, asks nothing.
            Assert.False(CloudRead.Refused(en, 403, """{"code":"ai_consent_required","consent":{"given":false}}""").AsksConsent);
            Assert.False(Refusal.Of(CloudRead.Refused(en, 403, """{"code":"ai_consent_required"}"""), "x").Consent);
        }
        finally { CloudRead.ConsentRequired -= Ask; }
    }

    [Fact]
    public void Suggestions_read_on_unless_tafs_cloud_says_off()
    {
        Assert.Equal(new Suggestions(false, "Coach won't offer recipes."), Suggestions.Read(J("""{"suggestions":{"show":false,"reason":"off","words":"Coach won't offer recipes."}}""")));
        Assert.True(Suggestions.Read(J("""{"suggestions":{"reason":"on"}}"""))!.Show);
        Assert.Null(Suggestions.Read(J("[]")));
        Assert.Equal("""{"show":true}""", Encoding.UTF8.GetString(Suggestions.Body(true)));
    }
}

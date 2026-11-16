using System.Text;
using System.Text.Json;
using Tafs.Core.Account;

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

    [Fact]
    public void Suggestions_read_on_unless_tafs_cloud_says_off()
    {
        Assert.Equal(new Suggestions(false, "Coach won't offer recipes."), Suggestions.Read(J("""{"suggestions":{"show":false,"reason":"off","words":"Coach won't offer recipes."}}""")));
        Assert.True(Suggestions.Read(J("""{"suggestions":{"reason":"on"}}"""))!.Show);
        Assert.Null(Suggestions.Read(J("[]")));
        Assert.Equal("""{"show":true}""", Encoding.UTF8.GetString(Suggestions.Body(true)));
    }
}

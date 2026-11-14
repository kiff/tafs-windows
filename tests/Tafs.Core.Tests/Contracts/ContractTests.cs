using Tafs.Core.Contracts;
using Tafs.Core.Tests.Strings;

namespace Tafs.Core.Tests.Contracts;

/// <summary>
/// What tafs cloud answers, read with this app's own types from the fixtures at the pin. A change
/// in an answer's shape fails here, in CI, never first on a screen.
/// </summary>
[Part("contracts")]
public class ContractTests
{
    static IReadOnlyList<ContractCase> Consent => ContractFile.Cases(SharedFiles.Contract("ai-consent.json"));

    [Fact]
    public void Consent_not_given_yet_decodes()
    {
        var c = Consent.First(x => x.Name == "not given yet");
        Assert.Equal(200, c.Status);
        var answer = Answers.Decode<ConsentAnswer>(c.Body);
        Assert.False(answer.Consent.Given);
        Assert.NotEmpty(answer.Consent.Words.Lines);
        Assert.NotEmpty(answer.Consent.Words.Allow);
    }

    [Fact]
    public void Consent_given_and_withdrawn_decode()
    {
        Assert.True(Answers.Decode<ConsentAnswer>(Consent.First(x => x.Name == "given").Body).Consent.Given);
        Assert.False(Answers.Decode<ConsentAnswer>(Consent.First(x => x.Name == "withdrawn").Body).Consent.Given);
    }

    [Fact]
    public void A_turn_refused_for_want_of_consent_says_why()
    {
        var c = Consent.First(x => x.Status == 403);
        var refusal = Answers.Decode<ConsentRefusal>(c.Body);
        Assert.Equal("ai_consent_required", refusal.Code);
        Assert.False(refusal.Consent.Given);
    }

    [Fact]
    public void A_yes_from_an_older_screen_is_a_409_with_the_current_consent()
    {
        var c = Consent.First(x => x.Status == 409);
        Assert.Equal("POST", c.Method);
        var refusal = Answers.Decode<ConsentRefusal>(c.Body);
        Assert.Null(refusal.Code);
        Assert.NotEmpty(refusal.Consent.Version);
    }

    [Fact]
    public void Every_consent_case_decodes_with_the_type_its_status_asks_for()
    {
        foreach (var c in Consent)
        {
            if (c.Status == 200) Answers.Decode<ConsentAnswer>(c.Body);
            else Answers.Decode<ConsentRefusal>(c.Body);
        }
    }

    [Fact]
    public void Coachs_first_start_decodes_with_the_draft_to_sign_once()
    {
        var start = Answers.Decode<SetupAnswer>(ContractFile.Answer(SharedFiles.Contract("setup-start.json"))).Start;
        Assert.NotNull(start);
        Assert.NotEmpty(start!.Requests);
        Assert.All(start.Requests, r => Assert.StartsWith("/", r.Path));
        Assert.NotEmpty(start.Words.Start);
        Assert.NotEmpty(start.CoachToolsStep.Give);
    }

    [Fact]
    public void The_draft_alone_decodes()
    {
        var start = Answers.Decode<SetupAnswer>(ContractFile.Answer(SharedFiles.Contract("setup-start-draft-alone.json"))).Start;
        Assert.NotNull(start);
        Assert.NotEmpty(start!.Requests);
    }

    [Fact]
    public void A_new_account_s_time_zone_is_set_and_said()
    {
        var answer = Answers.Decode<SetupAnswer>(ContractFile.Answer(SharedFiles.Contract("setup-time-zone.json")));
        Assert.Equal("Europe/Paris", answer.TimeZone);
        Assert.Null(answer.Start);
    }

    [Fact]
    public void Homes_part_of_the_recipes_decodes()
    {
        var home = Answers.Decode<RecipesHome>(ContractFile.Answer(SharedFiles.Contract("recipes-home.json")));
        Assert.True(home.Home.Shown);
        Assert.NotEmpty(home.Home.OwnWords.Title);
        Assert.NotEmpty(home.Suggestions.Words);
    }

    [Fact]
    public void An_answer_missing_a_field_the_app_needs_fails_to_decode() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            Answers.Decode<ConsentAnswer>(System.Text.Json.JsonDocument.Parse("""{"consent":{"given":false}}""").RootElement));
}

using System.Text;
using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>
/// Coach's cards in its thread, and a questions card's answers, as the iPad's SetupComponent, fold, items, answer(to:),
/// QuestionAnswers and QuestionSelection. As for the thread, the answers are shaped as the iPad's decoders read them.
/// </summary>
[Part("coach")]
public class CoachCardsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    const string Questions = """
        {"type": "questions", "id": "q1", "title": "A few things first", "text": "Before Rae starts:", "waits": "owner",
         "questions": [
           {"id": "mode", "text": "Live or test?", "options": [{"value": "live", "label": "Live money"}, {"value": "test", "label": "Test mode", "description": "Nothing real"}]},
           {"id": "days", "text": "Which days?", "multi": true, "options": [{"value": "mon", "label": "Monday"}, {"value": "tue", "label": "Tuesday"}], "note": "Pick any"}]}
        """;

    static CoachThread Thread(string components, string messages = "") => CoachThread.Read(JsonDocument.Parse($$"""
        {"messages": [
          {"at": "2026-11-15T09:00:00Z", "from": "owner", "text": "Set up refunds", "session": "s1"},
          {"at": "2026-11-15T09:00:10Z", "from": "taf", "text": "A few questions.", "session": "s1"},
          {"at": "2026-11-15T09:00:12Z", "from": "taf", "text": "Answer them and I'll go on.", "session": "s1"}{{messages}}],
         "components": [{{components}}]}
        """).RootElement)!;

    static string Shown(string component, string at = "2026-11-15T09:00:05Z", string answer = "") =>
        $$"""{"at": "{{at}}", "session": "s1", "run": "r1", "component": {{component}}{{answer}}}""";

    [Fact]
    public void A_questions_card_reads_with_its_questions_and_choices()
    {
        var card = Assert.Single(Thread(Shown(Questions)).Shown).Card;

        Assert.Equal(("questions", "q1", "A few things first", "owner"), (card.Kind, card.Id, card.Title, card.Waits));
        Assert.Equal(new[] { "mode", "days" }, card.Questions!.Select(q => q.Id));
        Assert.Equal(new CardChoice("test", "Test mode", "Nothing real"), card.Questions![0].Options[1]);
        Assert.True(card.Questions[1].Multi);
        Assert.Equal("Pick any", card.Questions[1].Note);
    }

    /// <summary>As the iPad's decoder: a card it can't read is left out, never the thread with it.</summary>
    [Fact]
    public void A_card_it_can_t_read_is_left_out()
    {
        var thread = Thread(string.Join(",", Shown("""{"type": "poll", "id": "x"}"""), Shown("""{"type": "notice"}"""),
            Shown("""{"type": "questions", "id": "q", "questions": [{"id": "a", "text": "?", "options": [{"value": "v"}]}]}"""),
            """{"session": "s1", "run": "r1", "component": {"type": "notice", "id": "n"}}""", Shown("""{"type": "notice", "id": "n2"}""")));

        Assert.Equal("n2", Assert.Single(thread.Shown).Card.Id);
    }

    /// <summary>As the iPad's fold: a card shown again replaces its earlier version in place, and waits anew.</summary>
    [Fact]
    public void A_card_shown_again_replaces_itself_in_place()
    {
        var thread = Thread(string.Join(",", Shown(Questions, answer: """, "answer": {"state": "answered", "words": "x"}"""),
            Shown("""{"type": "notice", "id": "n1"}"""), Shown(Questions.Replace("A few things first", "Again"), "2026-11-15T09:30:00Z")));

        Assert.Equal(new[] { "q1", "n1" }, thread.Shown.Select(c => c.Card.Id));
        Assert.Equal(("Again", (CardAnswer?)null), (thread.Shown[0].Card.Title, thread.Shown[0].Answer));
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 9, 0, 5, TimeSpan.Zero), thread.Shown[0].FirstAt);
    }

    /// <summary>As the iPad's placedAt: a card its run waits on comes after Coach's words with it; another at its own time.</summary>
    [Fact]
    public void A_card_its_run_waits_on_comes_after_Coach_s_words_with_it()
    {
        var waits = Thread(Shown(Questions)).Items;
        Assert.Equal(new[] { "Set up refunds", "A few questions.", "Answer them and I'll go on.", "q1" },
            waits.Select(i => i.Message?.Text ?? i.Card!.Card.Id));

        var notice = Thread(Shown("""{"type": "notice", "id": "n1"}""")).Items;
        Assert.Equal("n1", notice[1].Card?.Card.Id);
    }

    /// <summary>An answer to a card the thread shows reads once, in its card; the person's bubble repeats it only when it failed.</summary>
    [Fact]
    public void An_answer_reads_once_in_its_card()
    {
        var thread = Thread(Shown(Questions), """
            ,
            {"at": "2026-11-15T09:05:00Z", "from": "owner", "text": "[questions q1] {\"days\":{\"values\":[\"mon\",\"tue\"]},\"mode\":{\"value\":\"test\"}}", "session": "s1"},
            {"at": "2026-11-15T09:06:00Z", "from": "owner", "text": "[questions q9] failed: no", "session": "s1"}
            """);

        Assert.DoesNotContain(thread.Drawn("questions"), i => i.Message?.Text.StartsWith("[questions q1]") == true);
        Assert.Contains(thread.Drawn("questions"), i => i.Message?.Text.StartsWith("[questions q9]") == true);
        // Until this app draws a kind, its cards are left out and their answers keep their bubbles.
        Assert.Contains(thread.Drawn(), i => i.Message?.Text.StartsWith("[questions q1]") == true);
        Assert.DoesNotContain(thread.Drawn(), i => i.Card is not null);
        var answer = thread.AnswerTo("q1")!;
        Assert.Equal("Test mode · Monday, Tuesday", QuestionAnswers.Words(En, answer.Text, thread.Shown[0].Card.Questions!));
        Assert.Equal("Test mode · Monday, Tuesday", CoachChat.OwnerWords(En, "[questions q1] " + answer.Text, thread.Shown));
    }

    [Fact]
    public void A_recorded_answer_wins_over_a_message()
    {
        var thread = Thread(Shown(Questions, answer: """, "answer": {"state": "answered", "words": "Test mode · Monday", "at": "2026-11-15T09:05:00Z"}"""));

        Assert.Equal(new CardReply("questions", "q1", "Test mode · Monday", "Test mode · Monday"), thread.AnswerTo("q1"));
        var expired = Thread(Shown(Questions, answer: """, "answer": {"state": "expired", "words": "x"}"""));
        Assert.Null(expired.AnswerTo("q1"));
    }

    /// <summary>As the iPad's QuestionSelection: one choice, several on a multi question, or the person's own words, never both.</summary>
    [Fact]
    public void A_question_s_answer_is_its_choices_or_the_person_s_own_words()
    {
        var card = Assert.Single(Thread(Shown(Questions)).Shown).Card;
        var (mode, days) = (card.Questions![0], card.Questions[1]);
        var pick = new QuestionSelection();

        pick.Pick(mode, "live");
        pick.Pick(mode, "test");
        pick.Pick(days, "tue");
        pick.Pick(days, "mon");
        Assert.Equal(new QuestionAnswer.Choice("test"), pick.Answer(mode));
        Assert.Equal(new[] { "mon", "tue" }, ((QuestionAnswer.Choices)pick.Answer(days)!).Values);

        pick.ToggleOwn(mode);
        Assert.Null(pick.Answer(mode));
        pick.SetOwn(mode, "  300 € on weekdays ");
        Assert.Equal(new QuestionAnswer.Own("300 € on weekdays"), pick.Answer(mode));
        pick.Pick(mode, "live");
        Assert.Equal(new QuestionAnswer.Choice("live"), pick.Answer(mode));
    }

    /// <summary>As the iPad's encode: keys sorted, slashes and accents as they are, so the same answers send the same words.</summary>
    [Fact]
    public void Answers_encode_with_their_keys_sorted_and_read_back_in_the_person_s_words()
    {
        var text = QuestionAnswers.Encode(new Dictionary<string, QuestionAnswer>
        {
            ["mode"] = new QuestionAnswer.Own("300 €/day"),
            ["days"] = new QuestionAnswer.Choices(["mon"]),
        });

        Assert.Equal("""{"days":{"values":["mon"]},"mode":{"own":"300 €/day"}}""", text);
        Assert.Equal("Monday · " + En.Get("coach.answer.in_your_words", ("text", "300 €/day")),
            QuestionAnswers.Words(En, text, [new CardQuestion("days", "?", [new CardChoice("mon", "Monday")]), new CardQuestion("mode", "?", [])]));
        Assert.Null(QuestionAnswers.Words(En, "not json", []));
    }

    /// <summary>As the iPad's answerCard: the card's answers as data, with the conversation it's in, to its own path.</summary>
    [Fact]
    public void A_questions_card_s_answer_goes_as_data_to_its_card()
    {
        var shown = Assert.Single(Thread(Shown(Questions)).Shown);

        var (path, body) = CoachChat.AnswerRequest("taf-coach", shown, """{"mode":{"value":"test"}}""")!.Value;

        Assert.Equal("/v1/tafs/taf-coach/components/q1/answer", path);
        Assert.Equal("""{"answer":{"mode":{"value":"test"}},"session":"s1"}""", Encoding.UTF8.GetString(body));
        Assert.Null(CoachChat.AnswerRequest("taf-coach", shown, "not answers"));
    }

    /// <summary>As the iPad's answer(_:_:): sent, read again on a 409, the consent's code kept apart, anything else not reached.</summary>
    [Fact]
    public void What_sending_an_answer_comes_to()
    {
        Assert.Equal(CardSent.Sent, CoachChat.Sent(CloudRead.Of(Json("""{"run": "r", "session": "s"}"""))));
        Assert.Equal(CardSent.ReadAgain, CoachChat.Sent(CloudRead.Refused(En, 409, """{"code": "ai_consent_required"}""")));
        var consent = CloudRead.Refused(En, 403, """{"code": "ai_consent_required", "words": "Allow it first."}""");
        Assert.Equal(("ai_consent_required", "Allow it first."), (consent.Code, consent.Why));
        Assert.Equal(CardSent.Consent, CoachChat.Sent(consent));
        Assert.Equal(CardSent.NotReached, CoachChat.Sent(CloudRead.Refused(En, 500, "")));
        Assert.Equal(CardSent.NotReached, CoachChat.Sent(CloudRead.Unreached(En)));
    }

    /// <summary>As the iPad's SetupThread.plan: the latest plan, pinned above the thread, never in it; a step it can't read fails it.</summary>
    [Fact]
    public void The_plan_is_pinned_above_the_thread_with_its_steps()
    {
        const string first = """{"type": "steps", "id": "p", "steps": [{"label": "Connect Stripe", "status": "active"}]}""";
        const string later = """{"type": "steps", "id": "p2", "title": "Refunds", "steps": [{"label": "Connect Stripe", "status": "done"}, {"label": "Draft the Card", "status": "pending"}]}""";
        var thread = Thread(Shown(first) + "," + Shown(later, "2026-11-15T09:00:20Z") + "," + Shown(Questions));

        Assert.Equal("p2", thread.Plan!.Card.Id);
        Assert.Equal(new[] { new PlanStep("Connect Stripe", "done"), new PlanStep("Draft the Card", "pending") }, thread.Plan.Card.Steps);
        Assert.DoesNotContain(thread.Items, i => i.Card?.Card.Kind == "steps");
        var broken = Thread(Shown("""{"type": "steps", "id": "p", "steps": [{"label": "Connect Stripe"}]}"""));
        Assert.Null(broken.Plan);
    }

    /// <summary>As the iPad's SetupPlan.current: the active step, else the first not done, else the last.</summary>
    [Fact]
    public void The_plan_is_at_its_active_step_else_the_first_not_done()
    {
        PlanStep Step(string status) => new(status, status);

        Assert.Equal("active", PlanStep.Current([Step("done"), Step("pending"), Step("active")])!.Status);
        Assert.Equal("failed", PlanStep.Current([Step("done"), Step("failed"), Step("pending")])!.Status);
        Assert.Equal("skipped", PlanStep.Current([Step("done"), Step("skipped")])!.Status);
        Assert.Null(PlanStep.Current([]));
    }
}

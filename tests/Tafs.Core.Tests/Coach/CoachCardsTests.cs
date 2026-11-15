using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>
/// Coach's cards in its thread, and a questions card's answers, as the iPad's SetupComponent, fold, items, answer(to:),
/// and QuestionAnswers.words. As for the thread, the answers are shaped as the iPad's decoders read them.
/// </summary>
[Part("coach")]
public class CoachCardsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

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

    /// <summary>As the iPad's QuestionAnswers.words: the card's order, its labels, the person's own words as theirs.</summary>
    [Fact]
    public void Answers_read_back_in_the_person_s_words()
    {
        const string text = """{"days":{"values":["mon"]},"mode":{"own":"300 €/day"}}""";
        Assert.Equal("Monday · " + En.Get("coach.answer.in_your_words", ("text", "300 €/day")),
            QuestionAnswers.Words(En, text, [new CardQuestion("days", "?", [new CardChoice("mon", "Monday")]), new CardQuestion("mode", "?", [])]));
        Assert.Null(QuestionAnswers.Words(En, "not json", []));
    }

    /// <summary>
    /// As the iPad's synthesized decoder: an optional field absent or null is none, one of another shape fails the card, even a
    /// field this app doesn't keep yet; an answer whose state, words or by isn't words leaves the card unanswered.
    /// </summary>
    [Fact]
    public void A_card_s_fields_are_read_with_the_iPad_s_shapes()
    {
        const string confirm = """{"type": "confirm", "id": "c1", "text": "Live?", "options": null, "title": null}""";
        Assert.Null(Assert.Single(Thread(Shown(confirm)).Shown).Card.Options);
        Assert.Empty(Thread(Shown("""{"type": "notice", "id": "n1", "title": 1}""")).Shown);
        Assert.Empty(Thread(Shown(Questions.Replace("\"multi\": true", "\"multi\": \"yes\""))).Shown);
        Assert.Empty(Thread(Shown("""{"type": "result", "id": "r1", "taf": {"name": "Rae"}}""")).Shown);
        Assert.Empty(Thread(Shown("""{"type": "approval", "id": "a1", "signs": true}""")).Shown);
        Assert.Single(Thread(Shown("""{"type": "result", "id": "r1", "version": 2, "tools": ["kiff_refund"], "taf": {"agent": "a", "name": "Rae", "unscheduled": null}}""")).Shown);

        var unanswered = Thread(Shown(Questions, answer: """, "answer": {"state": 1, "words": "Test mode"}"""));
        Assert.Null(Assert.Single(unanswered.Shown).Answer);
        var timeless = Thread(Shown(Questions, answer: """, "answer": {"words": "Test mode", "at": 5}"""));
        Assert.Equal(new CardAnswer("answered", "Test mode", null), Assert.Single(timeless.Shown).Answer);
    }
}

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
        Assert.Equal(CardSent.Sent, CoachChat.Sent(CloudRead.Of(Json("""{"run": "r", "session": "s", "status": "running"}"""))));
        Assert.Equal(CardSent.NotReached, CoachChat.Sent(CloudRead.Of(Json("""{"run": "r", "session": "s"}"""))));
        Assert.Equal(CardSent.NotReached, CoachChat.Sent(CloudRead.Of(Json("""{"run": "r", "session": "s", "status": 1}"""))));
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

    /// <summary>As the iPad's cardRef: an answer for an id goes to the card waiting under it, else the latest, with its conversation.</summary>
    [Fact]
    public void An_answer_goes_to_the_card_waiting_under_its_id()
    {
        var answered = Shown(Questions, answer: """, "answer": {"state": "answered", "words": "Test mode", "at": "2026-11-15T09:01:00Z"}""");
        var again = Shown(Questions, "2026-11-15T09:02:00Z").Replace("\"session\": \"s1\"", "\"session\": \"s2\"");
        var thread = Thread(answered + "," + again);

        Assert.Equal("s2#q1", thread.CardRef("q1"));
        Assert.Equal("s2", thread.CardFor("q1")!.Session);
        Assert.Equal("s1#q1", Thread(answered).CardRef("q1"));
        Assert.Equal("#q9", thread.CardRef("q9"));
    }

    /// <summary>As the iPad's mayUseCoach: KIFF's access to Setup decides; without it, members.manage; nothing read is no.</summary>
    [Fact]
    public void Who_may_use_Coach()
    {
        const string access = """{"agents": {"setup": {"use": false, "oversee": false, "manage": false}, "a1": {"use": true, "oversee": false, "manage": true, "groups": ["g"]}}}""";
        const string me = """{"tenant_id": "t", "tenant_slug": "acme", "capabilities": ["members.manage"]}""";

        Assert.False(CoachAccess.SetupUse(Json(access)));
        Assert.Null(CoachAccess.SetupUse(Json("""{"agents": {"a1": {"use": true, "oversee": false, "manage": true}}}""")));
        Assert.Null(CoachAccess.SetupUse(Json("""{"agents": {"setup": {"use": true}}}""")));
        Assert.Equal(new[] { "members.manage" }, CoachAccess.Capabilities(Json(me)));
        Assert.Null(CoachAccess.Capabilities(Json("""{"tenant_slug": "acme", "capabilities": ["members.manage"]}""")));

        Assert.False(CoachAccess.MayUseCoach(false, ["members.manage"]));
        Assert.True(CoachAccess.MayUseCoach(true, null));
        Assert.True(CoachAccess.MayUseCoach(null, ["members.manage"]));
        Assert.False(CoachAccess.MayUseCoach(null, ["cards.manage"]));
        Assert.False(CoachAccess.MayUseCoach(null, null));
    }

    const string Result = """{"type": "result", "id": "done", "text": "Refunds is ready.", "tools": ["list_requests_k3f9"], "taf": {"agent": "refunds-x", "name": "Refunds"}}""";

    static ShownCard OneResult(string component, string outcome = "") =>
        Assert.Single(Thread(Shown(component, answer: outcome.Length == 0 ? "" : $$""", "answer": {"state": "answered", "words": "", "by": "tafs cloud", "outcome": {{outcome}}}""")).Shown);

    /// <summary>As the iPad's handOver(_:listed:) and planNeeded: tafs cloud's outcome, else the list, else still setting up.</summary>
    [Fact]
    public void A_hand_over_stands_as_tafs_cloud_answered_it()
    {
        ShownCard Card(string outcome) => OneResult(Result, outcome);
        CoachThread On(ShownCard c) => new([], [], null, [c]);

        var setUp = Card("""{"taf_id": "taf-1"}""");
        Assert.Equal((HandOverState.SetUp, (string?)null), On(setUp).HandOver(setUp.Ref, listed: false));
        Assert.Equal(new CardTaf("refunds-x", "Refunds"), setUp.Card.Taf);
        Assert.Equal(new[] { "list_requests_k3f9" }, setUp.Card.Tools);

        var failed = Card("""{"failed": "no draft."}""");
        Assert.Equal((HandOverState.Failed, "no draft."), On(failed).HandOver(failed.Ref, listed: true));
        Assert.Equal("Refunds wasn't set up: no draft.", CoachChat.NotSetUp(En, "Refunds", "no draft."));

        var waiting = OneResult(Result);
        Assert.Equal(HandOverState.SettingUp, On(waiting).HandOver(waiting.Ref, listed: false).State);
        Assert.Equal(HandOverState.SetUp, On(waiting).HandOver(waiting.Ref, listed: true).State);

        var noPlan = Card("""{"taf_id": "taf-1", "plan_needed": {"words": " Choose a plan to start it. "}, "version": "x"}""");
        Assert.Equal("Choose a plan to start it.", On(noPlan).PlanNeeded(noPlan.Ref));
        Assert.Null(noPlan.Answer!.Outcome!.Version);
        Assert.Null(On(Card("""{"plan_needed": {"words": "  "}}""")).PlanNeeded(noPlan.Ref));
    }

    /// <summary>As the iPad's improvement: the version made, why none was, or still preparing.</summary>
    [Fact]
    public void An_improvement_is_made_failed_or_preparing()
    {
        const string improves = """{"type": "result", "id": "v", "text": "Weekends too.", "taf": {"agent": "rae", "name": "Rae", "improves": "taf-1", "note": "Adds weekends"}}""";
        CoachThread On(ShownCard c) => new([], [], null, [c]);

        var made = OneResult(improves, """{"version": 3}""");
        Assert.Equal((ImprovementState.Made, 3, (string?)null), On(made).Improvement(made.Ref));
        Assert.Equal(new CardTaf("rae", "Rae", "taf-1", "Adds weekends"), made.Card.Taf);
        var failed = OneResult(improves, """{"failed": "the Card didn't change"}""");
        Assert.Equal((ImprovementState.Failed, 0, "the Card didn't change"), On(failed).Improvement(failed.Ref));
        Assert.Equal("Rae's new version wasn't made: the Card didn't change.", CoachChat.NotImproved(En, "Rae", "the Card didn't change"));
        var preparing = OneResult(improves);
        Assert.Equal(ImprovementState.Preparing, On(preparing).Improvement(preparing.Ref).State);

        // Read again until tafs cloud settles it, often after the run has ended (the owner's test); not once it's settled, nor
        // for a card long past.
        var shownAt = preparing.FirstAt;
        Assert.True(On(preparing).AwaitsImprovement(shownAt.AddSeconds(3)));
        Assert.False(On(made).AwaitsImprovement(shownAt.AddSeconds(3)));
        Assert.False(On(failed).AwaitsImprovement(shownAt.AddSeconds(3)));
        Assert.False(On(preparing).AwaitsImprovement(shownAt.AddMinutes(11)));
    }

    /// <summary>As the iPad's KIFFMapping.toolWords: after its server and dots, a generated prefix dropped, words for the rest.</summary>
    [Theory]
    [InlineData("list_requests_k3f9", "list requests k3f9")]
    [InlineData("stripe__refunds.create_refund", "create refund")]
    [InlineData("k3f9a2_issue-refund", "issue refund")]
    [InlineData("abcdef_x", "abcdef x")]
    [InlineData("k3f9a2_", "k3f9a2")]
    [InlineData("___", "___")]
    public void A_tool_reads_in_words(string tool, string words) => Assert.Equal(words, CoachChat.ToolWords(tool));

    /// <summary>A hand-over set up but not listed asks for Home once; listed, failed, an improvement or still setting up, never.</summary>
    [Fact]
    public void A_set_up_taf_not_listed_reads_Home_again_once()
    {
        var setUp = OneResult(Result, """{"taf_id": "taf-1"}""");
        var thread = new CoachThread([], [], null, [setUp]);
        var asked = new HashSet<string>();

        Assert.True(thread.ReadHomeFor(["rae"], asked));
        Assert.False(thread.ReadHomeFor(["rae"], asked));
        Assert.False(thread.ReadHomeFor(["refunds-x"], new HashSet<string>()));
        Assert.False(new CoachThread([], [], null, [OneResult(Result)]).ReadHomeFor([], new HashSet<string>()));
        Assert.False(new CoachThread([], [], null, [OneResult(Result, """{"failed": "no"}""")]).ReadHomeFor([], new HashSet<string>()));
    }

    /// <summary>As the iPad's answerValue for a confirm: its option's value as words, to its card; an empty one isn't an answer.</summary>
    [Fact]
    public void A_confirm_s_answer_is_its_option_s_value()
    {
        var shown = Assert.Single(Thread(Shown("""{"type": "confirm", "id": "c1", "text": "Live?", "options": [{"value": "live", "label": "Live money"}]}""")).Shown);

        var (path, body) = CoachChat.AnswerRequest("taf-coach", shown, "live")!.Value;

        Assert.Equal("/v1/tafs/taf-coach/components/c1/answer", path);
        Assert.Equal("""{"answer":"live","session":"s1"}""", Encoding.UTF8.GetString(body));
        Assert.Null(CoachChat.AnswerRequest("taf-coach", shown, ""));
        Assert.Equal("Live money", CoachChat.OwnerWords(En, "[confirm c1] live", [shown]));
    }

    /// <summary>
    /// As the iPad's answerValue for an approval: "approved" whatever follows "approved,", "declined", one signed earlier
    /// ("already approved: …") or "failed: why" as written; anything else isn't an answer.
    /// </summary>
    [Fact]
    public void An_approval_s_answer_is_its_outcome()
    {
        var shown = Assert.Single(Thread(Shown("""{"type": "approval", "id": "connect-stripe", "grant": "g_01"}""")).Shown);
        string? Answer(string text) => CoachChat.AnswerRequest("taf-coach", shown, text) is { } r
            ? JsonDocument.Parse(r.Body).RootElement.GetProperty("answer").GetString() : null;

        var (path, body) = CoachChat.AnswerRequest("taf-coach", shown, "declined")!.Value;
        Assert.Equal("/v1/tafs/taf-coach/components/connect-stripe/answer", path);
        Assert.Equal("""{"answer":"declined","session":"s1"}""", Encoding.UTF8.GetString(body));

        Assert.Equal("approved", Answer("approved"));
        Assert.Equal("approved", Answer("approved, signed in"));
        Assert.Equal("declined", Answer("declined"));
        Assert.Equal("already approved: Stripe is connected", Answer("already approved: Stripe is connected"));
        Assert.Equal("failed: planque didn't answer", Answer("failed: planque didn't answer"));
        foreach (var text in new[] { "", "approve", "approvedx", "Approved", "Declined", "declined, later", "not now", "signed in", "failed", "already approved" })
            Assert.Null(Answer(text));
    }
}

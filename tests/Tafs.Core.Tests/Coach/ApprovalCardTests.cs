using System.Text;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Coach;
using Tafs.Core.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>Coach's approval card, as the iPad's SetupApproval and setupCardState: what it says and where it stands.</summary>
[Part("coach")]
public class ApprovalCardTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static CoachThread Thread(params string[] shown) => CoachThread.Read(JsonDocument.Parse($$"""
        {"messages": [{"at": "2026-11-15T09:00:00Z", "from": "owner", "text": "Set up refunds", "session": "s1"}],
         "components": [{{string.Join(", ", shown)}}]}
        """).RootElement)!;

    static string Approval(string id = "a1", string at = "2026-11-15T09:00:05Z", string more = "", string answer = "") =>
        $$$"""{"at": "{{{at}}}", "session": "s1", "run": "r1", "component": {"type": "approval", "id": "{{{id}}}", "grant": "g_01", "draft": "d_01", "service": "mcp.stripe.com"{{{more}}}}{{{answer}}}}""";

    static string Answered(string words) => $$""", "answer": {"state": "answered", "words": "{{words}}"}""";

    static PlanqueGrant Grant(string status = "pending", string? reason = null) =>
        PlanqueGrants.Read(Encoding.UTF8.GetBytes($$"""{"grants": [{"id": "g_01", "status": "{{status}}", "text": "t", "digest": "d", "reason": {{(reason is null ? "null" : $"\"{reason}\"")}}, "request": {"server": "https://mcp.stripe.com", "agent": "refunds", "tools": []}, "vendor": "Stripe", "events": []}]}"""))![0];

    static AgentDraft Draft(string status = "draft", string more = "") =>
        KiffDrafts.Read(JsonDocument.Parse($$"""{"id": "d_01", "agent_id": "agt", "status": "{{status}}", "version": "v1", "label": "Refunds"{{more}}}""").RootElement)!;

    /// <summary>An approval keeps its grant, draft, service, tafs cloud's done line and its mark, read leniently.</summary>
    [Fact]
    public void An_approval_card_keeps_what_it_names()
    {
        var c = Thread(Approval(more: """, "connection": {"done": true, "words": "Stripe is connected"}, "signs": {"card": false, "words": " Its Card: next ", "takes": 3}""")).Shown[0].Card;

        Assert.Equal(("g_01", "d_01", "mcp.stripe.com", "Stripe is connected"), (c.Grant, c.Draft, c.Service, c.ConnectionDone));
        Assert.Equal(new CardSigns(false, " Its Card: next ", null), c.Signs);
        Assert.True(c.Signs!.CardNext);
        var blank = Thread(Approval(more: """, "connection": {"done": true, "words": " "}, "signs": {"words": "  "}""")).Shown[0].Card;
        Assert.Equal((null, null), (blank.ConnectionDone, blank.Signs!.Words));
    }

    /// <summary>
    /// A sign-in wider than the job: tafs cloud's words, kept as sent less their spaces; blank, or of another
    /// shape, is no line and never fails the card.
    /// </summary>
    [Fact]
    public void A_sign_in_wider_than_the_job_keeps_tafs_cloud_s_words()
    {
        const string words = "The sign-in for Google Sheets can't be narrowed to this job: it asks for more access than the taf uses.";
        Assert.Equal(words, Thread(Approval(more: $$""", "wider": {"words": " {{words}} "}""")).Shown[0].Card.Wider);
        Assert.Null(Thread(Approval(more: """, "wider": {"words": "  "}""")).Shown[0].Card.Wider);
        Assert.Null(Thread(Approval(more: """, "wider": true""")).Shown[0].Card.Wider);
        Assert.Null(Thread(Approval()).Shown[0].Card.Wider);
    }

    /// <summary>As the iPad's setupCardState: done only by its answer, failed with why, planque's failure with its reason.</summary>
    [Theory]
    [InlineData("", "pending", null, ApprovalState.NeedsYou, null)]
    [InlineData("Approved", "connected", null, ApprovalState.Done, null)]
    [InlineData("Declined", "pending", null, ApprovalState.Declined, null)]
    [InlineData("Approve · it didn't go through: the vendor refused", "pending", null, ApprovalState.Failed, "the vendor refused")]
    [InlineData("", "failed", "signed out", ApprovalState.Failed, "signed out")]
    [InlineData("Approved", "disconnected", "removed", ApprovalState.Failed, "removed")]
    [InlineData("", "connected", null, ApprovalState.NeedsYou, null)]
    public void Where_an_approval_stands(string words, string status, string? reason, ApprovalState state, string? why)
    {
        var thread = Thread(Approval(answer: words.Length == 0 ? "" : Answered(words)));
        Assert.Equal((state, why), ApprovalCard.State(thread, thread.Shown[0].Card, Grant(status, reason)));
    }

    /// <summary>
    /// A refused or failed approval never reads as approved (the owner's test: "Approved · signed in to Stripe" after Reject, in
    /// French): from tafs cloud's language-free answer when it sends one; without it, from its words, approved only for exactly
    /// tafs cloud's English "Approved", never by default.
    /// </summary>
    [Theory]
    [InlineData("Declined", null)]
    [InlineData("Refusé", null)]
    [InlineData("Abgelehnt", null)]
    [InlineData("Rechazado", null)]
    [InlineData("Rifiutato", null)]
    [InlineData("Geweigerd", null)]
    [InlineData("Odrzucone", null)]
    [InlineData("Recusado", null)]
    [InlineData("Approuver · cela n'a pas abouti : le fournisseur a refusé", null)]
    [InlineData("Aprobado", "declined")]
    [InlineData("Approved", "declined")]
    [InlineData("Approuvé", "failed: the vendor refused")]
    public void A_refused_or_failed_approval_never_reads_approved(string words, string? answer)
    {
        var recorded = answer is null ? Answered(words) : $$""", "answer": {"state": "answered", "words": "{{words}}", "answer": "{{answer}}"}""";
        var thread = Thread(Approval(answer: recorded));
        var c = thread.Shown[0].Card;
        var grant = Grant("listed");
        var (state, _) = ApprovalCard.State(thread, c, grant);
        Assert.NotEqual(ApprovalState.Done, state);
        foreach (var language in new[] { "en", "fr", "de", "es", "it", "nl", "pl", "pt" })
        {
            var w = WordBook.Load(SharedFiles.Strings, language);
            var line = ApprovalCard.Outcome(w, thread, c, state, grant, null);
            Assert.NotEqual(w.Get("coach.approved_signed", ("vendor", "Stripe")), line);
            Assert.NotEqual(w.Get("coach.answer.approved_connected", ("vendor", "Stripe")), line);
        }
    }

    /// <summary>tafs cloud's settled answers, as decided: failed reads failed with its reason; connected reads done.</summary>
    [Theory]
    [InlineData("failed: the vendor refused", "Approuver · cela n'a pas abouti : le fournisseur a refusé", ApprovalState.Failed, "the vendor refused")]
    [InlineData("connected", "Stripe est connecté ; sa Card vient ensuite", ApprovalState.Done, null)]
    [InlineData("already approved", "Déjà approuvé", ApprovalState.Done, null)]
    [InlineData("declined", "Refusé", ApprovalState.Declined, null)]
    public void Tafs_cloud_s_settled_answers(string answer, string words, ApprovalState state, string? why)
    {
        var thread = Thread(Approval(answer: $$""", "answer": {"state": "answered", "words": "{{words}}", "answer": "{{answer}}"}"""));
        Assert.Equal((state, why), ApprovalCard.State(thread, thread.Shown[0].Card, Grant("pending")));
    }

    /// <summary>tafs cloud's stored answer decides, whatever the words' language.</summary>
    [Fact]
    public void The_stored_answer_decides_whatever_the_language()
    {
        var thread = Thread(Approval(answer: """, "answer": {"state": "answered", "words": "Approuvé", "answer": "approved"}"""));
        Assert.Equal(ApprovalState.Done, ApprovalCard.State(thread, thread.Shown[0].Card, Grant("connected")).State);
        var unknown = Thread(Approval(answer: """, "answer": {"state": "answered", "words": "Approuvé", "answer": "maybe"}"""));
        Assert.NotEqual(ApprovalState.Done, ApprovalCard.State(unknown, unknown.Shown[0].Card, Grant("connected")).State);
    }

    /// <summary>A newer approval for the same connection makes the earlier one history, saying what was done with it.</summary>
    [Fact]
    public void A_newer_request_replaces_an_earlier_one()
    {
        var thread = Thread(Approval("a1", answer: Answered("Approved")), Approval("a2", at: "2026-11-15T09:01:00Z"));

        Assert.Equal((true, false), (ApprovalCard.Replaced(thread, "a1"), ApprovalCard.Replaced(thread, "a2")));
        Assert.Equal("Approved · Setup asked for more below", ApprovalCard.ReplacedLine(En, thread, "a1"));
        Assert.Equal("Setup changed this request: see below", ApprovalCard.ReplacedLine(En, Thread(Approval("a1"), Approval("a2", at: "2026-11-15T09:01:00Z")), "a1"));
    }

    /// <summary>As the iPad's outcome: a signed Card first, Coach's title, the draft's Card, else the vendor.</summary>
    [Fact]
    public void An_answered_approval_reads_as_one_line()
    {
        var done = Thread(Approval(answer: Answered("Approved")));
        var c = done.Shown[0].Card;

        Assert.Equal("Approved · Refunds' Card", ApprovalCard.Outcome(En, done, c, ApprovalState.Done, Grant("connected"), Draft("confirmed")));
        Assert.Equal("Approved · Refunds' Card", ApprovalCard.Outcome(En, done, c, ApprovalState.Done, Grant("connected"), Draft()));
        Assert.Equal("Approved · Stripe is connected", ApprovalCard.Outcome(En, done, c, ApprovalState.Done, Grant("connected"), Draft(more: """, "no_tools": true""")));
        Assert.Equal("Approved · signed in to Stripe", ApprovalCard.Outcome(En, done, c, ApprovalState.Done, null, null));
        Assert.Equal("Declined · Stripe", ApprovalCard.Outcome(En, done, c, ApprovalState.Declined, Grant(), null));
        var titled = Thread(Approval(more: """, "title": " Connect Stripe " """, answer: Answered("Approved")));
        Assert.Equal("Approved · Connect Stripe", ApprovalCard.Outcome(En, titled, titled.Shown[0].Card, ApprovalState.Done, Grant("connected"), null));
    }

    /// <summary>tafs cloud's "Card comes next" words: only on a card with no draft, not marked as signing the Card, not done.</summary>
    [Fact]
    public void A_Card_next_is_said_only_where_it_comes_next()
    {
        CoachCard Card(string more) => Thread($$$"""{"at": "2026-11-15T09:00:05Z", "session": "s1", "run": "r1", "component": {"type": "approval", "id": "a1"{{{more}}}}}""").Shown[0].Card;
        Assert.Equal("Its Card: next", ApprovalCard.CardNextWords(Card(""", "signs": {"card": false, "words": "Its Card: next"}""")));
        Assert.Null(ApprovalCard.CardNextWords(Card(""", "draft": "d", "signs": {"words": "Its Card: next"}""")));
        Assert.Null(ApprovalCard.CardNextWords(Card(""", "signs": {"card": true, "words": "Its Card: next"}""")));
        Assert.Null(ApprovalCard.CardNextWords(Card(""", "connection": {"done": true, "words": "Done"}, "signs": {"words": "Its Card: next"}""")));
    }

    /// <summary>As the iPad's vendorWords: a host reads as its name.</summary>
    [Theory]
    [InlineData("mcp.stripe.com", "Stripe")]
    [InlineData("notion-mcp.example.com", "Notion mcp")]
    [InlineData("google-sheets.com", "Google sheets")]
    [InlineData("Stripe", "Stripe")]
    public void A_vendor_s_host_reads_as_its_name(string host, string words) => Assert.Equal(words, ApprovalCard.VendorWords(host));
}

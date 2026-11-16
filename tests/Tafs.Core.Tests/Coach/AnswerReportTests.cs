using System.Text;
using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>Reporting one of Coach's answers, as the iPad's ReportAnswerSheet, canReport and reportAnswer.</summary>
[Part("coach")]
public class AnswerReportTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static readonly DateTimeOffset At = new(2026, 11, 15, 20, 0, 0, TimeSpan.Zero);

    static ChatMessage Answer(string? id = "msg_1", string text = "Refunds is set up.", ChatFrom from = ChatFrom.Taf, bool kickoff = false, string session = "s 1") =>
        new(At, from, kickoff, text, session, ServerId: id);

    static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>Coach's answers with words and tafs cloud's id, for a KIFF account; never the person's, a run's start or one still coming.</summary>
    [Fact]
    public void Which_answers_can_be_reported()
    {
        Assert.True(AnswerReport.Can(Answer(), kiff: true));
        Assert.False(AnswerReport.Can(Answer(), kiff: false));
        Assert.False(AnswerReport.Can(Answer(from: ChatFrom.Owner), kiff: true));
        Assert.False(AnswerReport.Can(Answer(kickoff: true), kiff: true));
        Assert.False(AnswerReport.Can(Answer(text: ""), kiff: true));
        Assert.False(AnswerReport.Can(Answer(id: null), kiff: true));
    }

    /// <summary>The route, escaped, and the exact body: the note trimmed, and left out when blank.</summary>
    [Fact]
    public void The_request_as_the_iPad_sends_it()
    {
        var (path, body) = AnswerReport.Request("taf/coach", Answer("m 2"), "wrong", "  It said 3 days.\n ");
        Assert.Equal("/v1/tafs/taf%2Fcoach/messages/m%202/report", path);
        Assert.Equal("""{"reason":"wrong","note":"It said 3 days.","session":"s 1"}""", Encoding.UTF8.GetString(body));
        Assert.Equal("""{"reason":"other","session":"s 1"}""", Encoding.UTF8.GetString(AnswerReport.Request("t", Answer(), "other", " \n ").Body));
        Assert.Equal("""{"reason":"harmful"}""", Encoding.UTF8.GetString(AnswerReport.Request("t", Answer(session: ""), "harmful", "").Body));
    }

    /// <summary>The reasons in the iPad's order, each with its words; a note past 1,000 characters as a person counts them.</summary>
    [Fact]
    public void Reasons_and_the_note_limit()
    {
        Assert.Equal(["Offensive", "Harmful", "Wrong", "Other"], AnswerReport.Reasons.Select(r => AnswerReport.Title(En, r)));
        Assert.False(AnswerReport.TooLong(new string('a', 1000)));
        Assert.True(AnswerReport.TooLong(new string('a', 1001)));
        // An emoji is one character to a person, two to UTF-16.
        Assert.False(AnswerReport.TooLong(string.Concat(Enumerable.Repeat("😀", 1000))));
    }

    /// <summary>new is sent, else reported already; a refusal says tafs cloud's words, a 404 that it's gone, anything else the app's.</summary>
    [Fact]
    public void What_tafs_cloud_s_answer_comes_to()
    {
        Assert.IsType<ReportOutcome.Sent>(AnswerReport.Outcome(En, CloudRead.Of(Json("""{"report":{"id":"r1","at":"2026-11-15T20:00:00Z","new":true}}"""))));
        Assert.IsType<ReportOutcome.Already>(AnswerReport.Outcome(En, CloudRead.Of(Json("""{"report":{"id":"r1","at":"2026-11-15T20:00:00Z","new":false}}"""))));
        Assert.Equal(new ReportOutcome.Failed("It didn't reach KIFF. Try again."), AnswerReport.Outcome(En, CloudRead.Of(Json("""{"report":{"id":"r1","new":true}}"""))));
        Assert.Equal(new ReportOutcome.Failed("Too many reports today."),
            AnswerReport.Outcome(En, CloudRead.Refused(En, 429, """{"error":"rate_limited","words":"Too many reports today."}""")));
        Assert.Equal(new ReportOutcome.Failed("This answer isn't on tafs cloud any more."),
            AnswerReport.Outcome(En, CloudRead.Refused(En, 404, """{"error":"message_not_found"}""")));
        Assert.Equal(new ReportOutcome.Failed("It didn't reach KIFF. Try again."),
            AnswerReport.Outcome(En, CloudRead.Refused(En, 500, """{"error":"internal"}""")));
        Assert.Equal(new ReportOutcome.Failed("It didn't reach KIFF. Try again."), AnswerReport.Outcome(En, CloudRead.Unreached(En)));
    }

    /// <summary>No Coach or no id can't be reported from here; an answer after the person signed out says so.</summary>
    [Fact]
    public async Task Sending_a_report()
    {
        (string Path, string Body)? sent = null;
        Task<CloudRead?> Send(string path, byte[] body)
        {
            sent = (path, Encoding.UTF8.GetString(body));
            return Task.FromResult<CloudRead?>(CloudRead.Of(Json("""{"report":{"id":"r1","at":"x","new":true}}""")));
        }
        Assert.IsType<ReportOutcome.Sent>(await AnswerReport.SendAsync(En, Send, "coach_1", Answer(), "offensive", ""));
        Assert.Equal(("/v1/tafs/coach_1/messages/msg_1/report", """{"reason":"offensive","session":"s 1"}"""), sent);
        var cant = new ReportOutcome.Failed("This answer can't be reported from here.");
        Assert.Equal(cant, await AnswerReport.SendAsync(En, Send, null, Answer(), "wrong", ""));
        Assert.Equal(cant, await AnswerReport.SendAsync(En, Send, "coach_1", Answer(id: null), "wrong", ""));
        Assert.Equal(new ReportOutcome.Failed("You signed out."),
            await AnswerReport.SendAsync(En, (_, _) => Task.FromResult<CloudRead?>(null), "coach_1", Answer(), "wrong", ""));
    }
}

using System.Text;
using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>Writing to a taf while it works (P34), as the iPad's submitChat and TafsCloud.steer.</summary>
[Part("record")]
public class TafSteerTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    [Fact]
    public void The_thread_says_its_run_at_work_and_what_waits()
    {
        var thread = Json("""
            {"messages": [], "working": {"session": "s1", "run": "run2"},
             "steering": {"run": "run2", "status": "PAUSED",
               "untaken": [{"text": "Use the EUR account", "at": "2026-11-17T09:00:00Z"}, {"text": "no time"}],
               "held": {"text": "Then email me", "at": "2026-11-17T09:01:00Z"},
               "not_sent": [{"text": "Skip Acme", "at": "2026-11-17T08:00:00Z", "why": "You stopped it before it read this."}]}}
            """);
        Assert.Equal("run2", TafSteer.WorkingRun(thread));
        var s = TafSteer.Read(thread)!;
        Assert.True(s.Paused);
        Assert.Equal(["Use the EUR account"], s.Untaken.Select(m => m.Text));
        Assert.Equal("Then email me", s.Held!.Text);
        Assert.Equal("You stopped it before it read this.", s.NotSent.Single().Why);
        var read = CoachThread.Read(thread)!;
        Assert.Equal(("run2", "run2"), (read.WorkingRun, read.Steering!.Run));

        Assert.Null(TafSteer.Read(Json("""{"messages": [], "steering": null}""")));
        Assert.Null(TafSteer.WorkingRun(Json("""{"messages": []}""")));
        Assert.Equal("Refunds reads this at its next step", TafSteer.ReadsLine(En, "Refunds", paused: false));
        Assert.Equal("Refunds reads this when it carries on", TafSteer.ReadsLine(En, "Refunds", paused: true));
    }

    [Fact]
    public async Task Words_go_to_the_run_once_with_hold_and_the_answer_reads_as_tafs_cloud_says()
    {
        string? sentPath = null, sentBody = null;
        Task<CloudRead> Send(string path, byte[] body, CloudRead answer)
        {
            (sentPath, sentBody) = (path, Encoding.UTF8.GetString(body));
            return Task.FromResult(answer);
        }
        var taken = await TafSteer.SteerAsync(En, "taf_1", "run2", "Use EUR", (p, b) => Send(p, b, CloudRead.Of(Json("""{"status": "RUNNING"}"""))));
        Assert.Equal(new Steered.Taken("RUNNING"), taken);
        Assert.Equal(("/v1/tafs/taf_1/steer", """{"run":"run2","text":"Use EUR","hold":true}"""), (sentPath, sentBody));

        Assert.Equal(new Steered.Kept(false, new SteerMessage("Then email me", DateTimeOffset.Parse("2026-11-17T09:01:00Z"))),
            TafSteer.Answer(En, CloudRead.Of(Json("""{"held": {"text": "Then email me", "at": "2026-11-17T09:01:00Z"}}"""))));
        Assert.Equal(new Steered.Kept(true, null), TafSteer.Answer(En, CloudRead.Of(Json("""{"sent": {"run": "run3"}}"""))));
    }

    [Fact]
    public void More_than_tafs_cloud_holds_stays_in_the_box_anything_else_is_not_sent()
    {
        Assert.Equal(new Steered.Full("Two messages already wait."),
            TafSteer.Answer(En, new CloudRead(null, "Two messages already wait.", 409, "held_full", "Two messages already wait.")));
        var refused = Assert.IsType<Steered.Refused>(TafSteer.Answer(En, new CloudRead(null, "Too busy.", 429)));
        Assert.Equal("Too busy.", refused.Why.Why);
        Assert.IsType<Steered.Refused>(TafSteer.Answer(En, new CloudRead(null, null, 0)));
    }
}

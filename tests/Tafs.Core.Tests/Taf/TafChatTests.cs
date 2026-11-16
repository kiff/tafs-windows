using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>
/// A taf's chat, as the iPad's loadChat, chatItems, tafActivityWords and the sample's greeting. tafs cloud has no contract for
/// a taf's conversation yet: the answers here are shaped as the iPad's decoder reads them, Coach's thread's shape.
/// </summary>
[Part("record")]
public class TafChatTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    const string Thread = """
        {"messages": [
          {"at": "2026-11-15T09:00:20Z", "from": "taf", "text": "Refunded #4471.", "session": "s1",
           "work": {"ms": 3200, "steps": [{"tool": "memory"}, {"tool": "k3f9a2_stripe_refund", "error": true}, {"tool": "k3f9a2_stripe_refund"}]},
           "delivered": "Sent to #support", "counted": "From the record: 1 refund, €40", "answers": null},
          {"at": "2026-11-15T09:00:00Z", "from": "owner", "text": "Refund #4471", "session": "s1", "id": "s1.in"},
          {"at": "2026-11-15T09:00:00Z", "from": "owner", "text": "Refund #4471", "session": "s1", "id": "s1.in"},
          {"at": "2026-11-15T07:00:00Z", "from": "owner", "text": "Every morning", "session": "s0", "kind": "kickoff"}]}
        """;

    static Func<string, Task<CloudRead>> Answers(string json, List<string>? asked = null) => path =>
    {
        asked?.Add(path);
        return Task.FromResult(CloudRead.Of(JsonDocument.Parse(json).RootElement));
    };

    /// <summary>Its thread is read from tafs cloud's conversation for the taf, its id escaped as a path part.</summary>
    [Fact]
    public async Task The_thread_is_read_from_the_taf_s_conversation()
    {
        var asked = new List<string>();
        var (thread, error) = await TafChat.LoadAsync(En, "taf/1", Answers(Thread, asked));
        Assert.Equal(["/v1/tafs/taf%2F1/conversation"], asked);
        Assert.Null(error);
        var items = TafChat.Items(thread!);
        // By time, each once: the run's start, the person's message, then the taf's answer.
        Assert.Equal(["Every morning", "Refund #4471", "Refunded #4471."], items.Select(i => i.Message!.Text));
        Assert.True(items[0].Message!.Kickoff);
        var answer = items[2].Message!;
        Assert.Equal(("Sent to #support", "From the record: 1 refund, €40"), (answer.Delivered, answer.Counted));
    }

    /// <summary>A thread not read says so, as the iPad's chatErrors: tafs cloud's words, not reached, or the app's own for one that doesn't decode.</summary>
    [Fact]
    public async Task A_thread_not_read_says_why()
    {
        var refused = await TafChat.LoadAsync(En, "t", _ => Task.FromResult(CloudRead.Refused(En, 403, """{"words": "Only its manager can read it."}""")));
        Assert.Equal((null, "The thread couldn't be loaded. Only its manager can read it."), refused);
        var (_, offline) = await TafChat.LoadAsync(En, "t", _ => Task.FromResult(CloudRead.Unreached(En)));
        Assert.Equal(En.Get("chat.thread_not_loaded", ("why", En.Get("error.offline"))), offline);
        var (none, undecoded) = await TafChat.LoadAsync(En, "t", Answers("""{"messages": [{"from": "taf"}]}"""));
        Assert.Null(none);
        Assert.Equal(En.Get("chat.thread_not_loaded", ("why", En.Get("error.ours"))), undecoded);
    }

    /// <summary>
    /// A taf's steps in its words, as the iPad's tafActivityWords: a connected tool by its title, its notes, what waits, else
    /// the tool's name in words; a failed try folded into the step that worked.
    /// </summary>
    [Fact]
    public void A_taf_s_steps_read_in_its_words()
    {
        Assert.Equal("Reading its notes…", TafChat.ActivityWords(En, "memory"));
        Assert.Equal("Checking what waits for you…", TafChat.ActivityWords(En, "kiff_pending"));
        Assert.Equal("Asking KIFF to run stripe refund…", TafChat.ActivityWords(En, "k3f9a2_stripe_refund"));
        Assert.Equal("Asking KIFF: Create refund…", TafChat.ActivityWords(En, "k3f9a2_stripe_refund", t => t == "k3f9a2_stripe_refund" ? "Create refund" : null));
        var rows = TafChat.WorkRows(En, new ChatWork(3200, [new("memory"), new("k3f9a2_stripe_refund", Error: true), new("k3f9a2_stripe_refund")]));
        Assert.Equal([("Reading its notes…", StepStatus.Done, 1), ("Asking KIFF to run stripe refund…", StepStatus.Done, 2)],
            rows.Select(r => (r.Words, r.Status, r.Tries)));
    }

    /// <summary>A sample taf greets the person with what it's doing, as the iPad's sampleHello; starting when it's doing nothing yet.</summary>
    [Fact]
    public void A_sample_taf_says_hello_with_what_it_s_doing()
    {
        var at = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
        var hello = Assert.Single(TafChat.Sample(En, "Refunds", "Refunded #4471", at));
        Assert.Equal((ChatFrom.Taf, false, at), (hello.From, hello.Kickoff, hello.At));
        Assert.Equal(En.Get("sample.hello", ("name", "Refunds"), ("now", "Refunded #4471")), hello.Text);
        Assert.Contains("Starting", Assert.Single(TafChat.Sample(En, "Deploys", "", at)).Text);
        Assert.StartsWith("Ask Refunds what it did", TafChat.Empty(En, "Refunds"));
        Assert.StartsWith("Ask it what it did", TafChat.Empty(En, null));
    }
}

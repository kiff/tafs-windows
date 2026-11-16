using System.Text;
using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>Writing to a taf, as the iPad's sendChat, SentMessages and stopChat.</summary>
[Part("home")]
public class TafChatSendTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    static ChatMessage Owner(string text, DateTimeOffset at, string? id = null) => new(at, ChatFrom.Owner, false, text, "s1", ServerId: id);
    static ChatMessage Taf(string text, DateTimeOffset at) => new(at, ChatFrom.Taf, false, text, "s1");

    /// <summary>An empty message sends nothing and says nothing; one past 2000 characters says so; the rest goes trimmed.</summary>
    [Fact]
    public void A_message_is_checked_before_anything_is_sent()
    {
        Assert.Equal((null, null), TafChatSend.Check(En, "   \n "));
        Assert.Equal(("Refund order 42", null), TafChatSend.Check(En, "  Refund order 42 \n"));
        var (text, why) = TafChatSend.Check(En, new string('a', 2001));
        Assert.Null(text);
        Assert.Equal("That's longer than 2000 characters. Say it in fewer words.", why);
        // Characters as the person sees them: 2000 emoji are within the limit.
        Assert.NotNull(TafChatSend.Check(En, string.Concat(Enumerable.Repeat("👍", 2000))).Text);
    }

    /// <summary>A message sent shows after the thread's latest, even with this PC's clock behind tafs cloud's.</summary>
    [Fact]
    public void A_sent_message_is_stamped_never_before_the_thread_s_latest()
    {
        Assert.Equal(Now, TafChatSend.Stamp(null, Now));
        Assert.Equal(Now, TafChatSend.Stamp(Now.AddSeconds(-5), Now));
        Assert.Equal(Now.AddSeconds(3).AddMilliseconds(1), TafChatSend.Stamp(Now.AddSeconds(3), Now));
        var pending = TafChatSend.Pending("Hi", [Taf("Hello", Now.AddSeconds(3))], Now);
        Assert.Equal((ChatFrom.Owner, "pending", Now.AddSeconds(3).AddMilliseconds(1)), (pending.From, pending.Session, pending.At));
    }

    /// <summary>
    /// A sent message stays on screen until the thread has it, matched once by its id, else by its words at or after it was
    /// sent; one older than two minutes is let go.
    /// </summary>
    [Fact]
    public void A_sent_message_stays_until_the_thread_has_it()
    {
        var sent = Owner("Refund order 42", Now);
        // Not in the thread yet: it's added, and still waits.
        var (shown, waiting) = TafChatSend.Merge([Taf("Hello", Now.AddSeconds(-30))], [sent], Now.AddSeconds(5));
        Assert.Equal(["Hello", "Refund order 42"], shown.Select(m => m.Text));
        Assert.Single(waiting);
        // The thread has it: shown once, and nothing waits.
        (shown, waiting) = TafChatSend.Merge([Owner("Refund order 42", Now.AddSeconds(1)), Taf("On it", Now.AddSeconds(2))], [sent], Now.AddSeconds(5));
        Assert.Equal(["Refund order 42", "On it"], shown.Select(m => m.Text));
        Assert.Empty(waiting);
        // The same words sent twice are matched one each.
        (shown, waiting) = TafChatSend.Merge([Owner("ok", Now.AddSeconds(1))], [Owner("ok", Now), Owner("ok", Now.AddSeconds(1))], Now.AddSeconds(2));
        Assert.Single(waiting);
        // Older than two minutes, it's let go.
        (_, waiting) = TafChatSend.Merge([], [sent], Now.AddMinutes(3));
        Assert.Empty(waiting);
        // By id when it has one, never by words.
        (_, waiting) = TafChatSend.Merge([Owner("Refund order 42", Now.AddSeconds(1), "m2")], [Owner("Refund order 42", Now, "m1")], Now.AddSeconds(2));
        Assert.Single(waiting);
    }

    /// <summary>Send and Stop go to tafs cloud's routes with their bodies; refused, they say why in the app's words.</summary>
    [Fact]
    public async Task Send_and_stop_go_to_tafs_cloud_and_say_why_when_refused()
    {
        var calls = new List<(string Path, string Body)>();
        Func<string, byte[], Task<CloudRead>> ok = (p, b) => { calls.Add((p, Encoding.UTF8.GetString(b))); return Task.FromResult(CloudRead.Of(JsonDocument.Parse("{}").RootElement)); };
        Assert.Null(await TafChatSend.SendAsync(En, "taf/1", "Hi you", ok));
        Assert.Null(await TafChatSend.StopAsync(En, "taf/1", "s9", ok));
        Assert.Equal(("/v1/tafs/taf%2F1/messages", """{"text":"Hi you"}"""), calls[0]);
        Assert.Equal(("/v1/tafs/taf%2F1/stop", """{"session":"s9"}"""), calls[1]);

        Func<string, byte[], Task<CloudRead>> refused = (_, _) => Task.FromResult(new CloudRead(null, "Too busy.", 429));
        Assert.Equal("The message wasn't sent. Too busy.", await TafChatSend.SendAsync(En, "t", "Hi", refused));
        Assert.Equal("Not stopped. Too busy.", await TafChatSend.StopAsync(En, "t", "s", refused));
    }
}

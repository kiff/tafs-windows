using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>A message tafs cloud didn't take, kept in the thread, as the iPad's UnsentBubble and retryChat.</summary>
[Part("record")]
public class UnsentTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    [Fact]
    public void It_says_not_sent_and_why_in_one_line() =>
        Assert.Equal("Not sent · Too busy.", Unsent.Line(En, UnsentMessage.Of("Hi", "Too busy.", DateTimeOffset.UnixEpoch)));

    /// <summary>
    /// A refusal for a halt names which (tafs cloud's <c>halted</c>): the account's Halt screen resumes it anywhere, a taf's only
    /// in its own chat; a word tafs cloud doesn't send, or another refusal, offers no Resume.
    /// </summary>
    [Fact]
    public void A_halted_refusal_is_resumed_on_its_own_halt_screen()
    {
        Assert.Equal("taf", CloudRead.Refused(En, 409, """{"words": "Refunds is halted.", "halted": "taf"}""").Halted);
        Assert.Null(CloudRead.Refused(En, 409, """{"words": "x", "halted": "everything"}""").Halted);
        Assert.Null(CloudRead.Refused(En, 503, """{"words": "x"}""").Halted);

        UnsentMessage M(string? halted) => UnsentMessage.Of("Hi", "x", DateTimeOffset.UnixEpoch, halted);
        Assert.Equal(HaltScreen.Account, Unsent.ResumesWith(M("account"), inTafChat: false));
        Assert.Equal(HaltScreen.Account, Unsent.ResumesWith(M("account"), inTafChat: true));
        Assert.Equal(HaltScreen.Taf, Unsent.ResumesWith(M("taf"), inTafChat: true));
        Assert.Null(Unsent.ResumesWith(M("taf"), inTafChat: false));
        Assert.Null(Unsent.ResumesWith(M(null), inTafChat: true));
    }

    /// <summary>Retry takes it out where it was; not tried at all, it goes back to the same place, never lost.</summary>
    [Fact]
    public void Retry_takes_it_out_and_puts_it_back_in_its_place()
    {
        var (a, b, c) = (UnsentMessage.Of("a", "x", DateTimeOffset.UnixEpoch), UnsentMessage.Of("b", "x", DateTimeOffset.UnixEpoch), UnsentMessage.Of("c", "x", DateTimeOffset.UnixEpoch));
        var (left, at) = Unsent.Take([a, b, c], b.Id);
        Assert.Equal([a, c], left);
        Assert.Equal(1, at);
        Assert.Equal([a, b, c], Unsent.PutBack(left, b, at));
        Assert.Equal([a, c, b], Unsent.PutBack(left, b, 9));
        var (still, none) = Unsent.Take([a], "gone");
        Assert.Equal([a], still);
        Assert.Equal(-1, none);
        Assert.NotEqual(a.Id, UnsentMessage.Of("a", "x", DateTimeOffset.UnixEpoch).Id);
    }
}

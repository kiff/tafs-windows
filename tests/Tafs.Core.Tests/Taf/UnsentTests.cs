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

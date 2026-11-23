using Tafs.Core.NeedsYou;

namespace Tafs.Core.Tests.NeedsYou;

/// <summary>The timed read of held calls, jittered and reading the capabilities only now and then (docs/scale.md, finding 6).</summary>
[Part("approvals")]
public class HeldWatchTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 23, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EachWaitIsAMinuteJitteredWithinTwentyPercent()
    {
        Assert.Equal(TimeSpan.FromSeconds(48), HeldWatch.Wait(0));
        Assert.Equal(TimeSpan.FromSeconds(60), HeldWatch.Wait(0.5));
        Assert.Equal(TimeSpan.FromSeconds(72), HeldWatch.Wait(1));
        Assert.Equal(TimeSpan.FromSeconds(72), HeldWatch.Wait(7));
    }

    [Fact]
    public void AReadSomeoneAskedForAlwaysReadsTheCapabilities()
    {
        Assert.True(HeldWatch.ReadsMe(timed: false, Now, Now));
        Assert.True(HeldWatch.ReadsMe(timed: false, null, Now));
    }

    [Fact]
    public void ATimedReadReadsThemOnlyWhenNoneWereReadOrFifteenMinutesPassed()
    {
        Assert.True(HeldWatch.ReadsMe(timed: true, null, Now));
        Assert.False(HeldWatch.ReadsMe(timed: true, Now, Now.AddMinutes(1)));
        Assert.False(HeldWatch.ReadsMe(timed: true, Now, Now.AddMinutes(14.9)));
        Assert.True(HeldWatch.ReadsMe(timed: true, Now, Now.AddMinutes(15)));
    }
}

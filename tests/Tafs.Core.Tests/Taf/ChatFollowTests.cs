using Tafs.Core.Coach;
using Tafs.Core.Taf;

namespace Tafs.Core.Tests.Taf;

/// <summary>A thread read again while it moves, backing off and stopping when it doesn't (docs/scale.md, finding 4).</summary>
[Part("record")]
public class ChatFollowTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 23, 2, 0, 0, TimeSpan.Zero);

    static CoachThread Thread(params string[] texts) =>
        new(texts.Select((t, i) => new ChatMessage(Now.AddSeconds(i), ChatFrom.Taf, false, t, "s1")).ToList(), [], "s1");

    [Fact]
    public void FastForAMinuteThenSlowThenSlowestThenStops()
    {
        var follow = new ChatFollow();
        Assert.Equal(ChatFollow.Fast, follow.Next("a", Now, 0.5));
        Assert.Equal(ChatFollow.Fast, follow.Next("a", Now.AddSeconds(59), 0.5));
        Assert.Equal(ChatFollow.Slow, follow.Next("a", Now.AddMinutes(1), 0.5));
        Assert.Equal(ChatFollow.Slow, follow.Next("a", Now.AddMinutes(9), 0.5));
        Assert.Equal(ChatFollow.Slowest, follow.Next("a", Now.AddMinutes(10), 0.5));
        Assert.Equal(ChatFollow.Slowest, follow.Next("a", Now.AddMinutes(29), 0.5));
        Assert.Null(follow.Next("a", Now.AddMinutes(30), 0.5));
    }

    [Fact]
    public void AChangeStartsOverFast()
    {
        var follow = new ChatFollow();
        follow.Next("a", Now, 0.5);
        Assert.Equal(ChatFollow.Slow, follow.Next("a", Now.AddMinutes(5), 0.5));
        Assert.Equal(ChatFollow.Fast, follow.Next("b", Now.AddMinutes(5), 0.5));
        Assert.Equal(ChatFollow.Fast, follow.Next("b", Now.AddMinutes(5.5), 0.5));
    }

    [Fact]
    public void ResetStartsOverFastEvenWithTheSameThread()
    {
        var follow = new ChatFollow();
        follow.Next("a", Now, 0.5);
        Assert.Null(follow.Next("a", Now.AddMinutes(31), 0.5));
        follow.Reset();
        Assert.Equal(ChatFollow.Fast, follow.Next("a", Now.AddMinutes(31), 0.5));
    }

    [Fact]
    public void EveryWaitIsJitteredWithinTwentyPercent()
    {
        var follow = new ChatFollow();
        Assert.Equal(TimeSpan.FromSeconds(1.6), follow.Next("a", Now, 0));
        Assert.Equal(TimeSpan.FromSeconds(2.4), follow.Next("a", Now, 1));
        Assert.Equal(TimeSpan.FromSeconds(12), follow.Next("a", Now.AddMinutes(2), 0));
        Assert.Equal(TimeSpan.FromSeconds(18), follow.Next("a", Now.AddMinutes(2), 1));
        Assert.Equal(TimeSpan.FromSeconds(48), follow.Next("a", Now.AddMinutes(15), 0));
        Assert.Equal(TimeSpan.FromSeconds(72), follow.Next("a", Now.AddMinutes(15), 5));
    }

    [Fact]
    public void TheSignatureMovesWithNewWordsARunOrAWaitingMessage()
    {
        var one = ChatFollow.Signature(Thread("Hel"), 0);
        Assert.Equal(one, ChatFollow.Signature(Thread("Hel"), 0));
        Assert.NotEqual(one, ChatFollow.Signature(Thread("Hello"), 0));
        Assert.NotEqual(one, ChatFollow.Signature(Thread("Hel", "Next"), 0));
        Assert.NotEqual(one, ChatFollow.Signature(Thread("Hel"), 1));
        Assert.NotEqual(one, ChatFollow.Signature(Thread("Hel") with { WorkingRun = "r2" }, 0));
        Assert.Equal("none|1", ChatFollow.Signature(null, 1));
    }
}

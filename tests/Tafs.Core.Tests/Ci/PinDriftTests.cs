using Tafs.Core.Ci;

namespace Tafs.Core.Tests.Ci;

public class PinDriftTests
{
    static readonly DateTimeOffset Head = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_pin_at_main_is_fine_however_old_main_is() =>
        Assert.True(PinDrift.Evaluate(Head.AddDays(-90), Head, 0).Ok);

    [Fact]
    public void Twenty_commits_and_seven_days_are_still_within_limits() =>
        Assert.True(PinDrift.Evaluate(Head.AddDays(-7), Head, 20).Ok);

    [Fact]
    public void Twenty_one_commits_behind_fails()
    {
        var r = PinDrift.Evaluate(Head.AddHours(-1), Head, 21);
        Assert.False(r.Ok);
        Assert.Contains("21 commits", r.Message);
    }

    [Fact]
    public void More_than_seven_days_behind_fails()
    {
        var r = PinDrift.Evaluate(Head.AddDays(-7).AddMinutes(-1), Head, 3);
        Assert.False(r.Ok);
        Assert.Contains("days behind", r.Message);
    }

    [Fact]
    public void Both_reasons_are_reported()
    {
        var r = PinDrift.Evaluate(Head.AddDays(-30), Head, 100);
        Assert.Contains("commits behind", r.Message);
        Assert.Contains("days behind", r.Message);
    }
}

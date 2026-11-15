using System.Text.Json;
using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.NeedsYou;

/// <summary>A held call's notification, worded as the iPad's HoldNotificationTests, and only for a call held since the last read.</summary>
[Part("approvals")]
public class HoldToastTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static HeldCall Call(string id = "ce_1", string? taf = "Rae") => KiffHeld.Calls(En, KiffHeld.Read(JsonDocument.Parse($$$"""
        {"exceptions":[{"id":"{{{id}}}","subject":"rae-agent","action":"refund_order","title":"Refund order","entity_id":"42","status":"held",
          "breaches":[{"outcome":"per_action_exceeded","reason":"Over €50 each"}],"reason_text":"Over €50 each",
          "parameters":{"amount":15000},"parameter_units":{"amount":{"unit":"EUR","scale":2}},
          "created_at":"2026-10-19T13:55:00Z","expires_at":"2026-10-19T14:05:00Z"}]}
        """).RootElement)!, _ => taf is null ? null : (taf, 1))[0];

    /// <summary>"Rae needs you" / "Refund order 42, €150.00 · Over €50 each. Answer by 14:05. Nothing is sent until you approve."</summary>
    [Fact]
    public void A_hold_s_notification_reads_as_the_iPad_s()
    {
        var lines = HoldToast.Lines(En, Call(), TimeZoneInfo.Utc)!;
        Assert.Equal("Rae needs you", lines.Title);
        Assert.Equal($"Refund order 42, {new KiffAmount(15000, "EUR", 2).Formatted(En)} · Over €50 each. Answer by 14:05. Nothing is sent until you approve.", lines.Body);
        Assert.Equal("ce_1", lines.Hold);
        // In this PC's time, as every screen here.
        var paris = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Paris");
        Assert.Contains("Answer by 16:05.", HoldToast.Lines(En, Call(), paris)!.Body);
    }

    /// <summary>A sample has no KIFF id, so it's never notified.</summary>
    [Fact]
    public void A_sample_is_never_notified() =>
        Assert.All(NeedsYouScreen.Sample(En, DateTimeOffset.UtcNow), c => Assert.Null(HoldToast.Lines(En, c, TimeZoneInfo.Utc)));

    /// <summary>The minute's read goes on while the window isn't minimised, and for ten minutes after it was; then it stops.</summary>
    [Fact]
    public void Minimised_for_more_than_ten_minutes_the_minute_s_read_stops()
    {
        var at = new DateTimeOffset(2026, 11, 15, 20, 0, 0, TimeSpan.Zero);
        Assert.True(HoldToast.Reads(null, at));
        Assert.True(HoldToast.Reads(at, at.AddMinutes(10)));
        Assert.False(HoldToast.Reads(at, at.AddMinutes(10).AddSeconds(1)));
    }

    /// <summary>The first read notifies nothing; after it, only calls whose ids weren't there before.</summary>
    [Fact]
    public void Only_a_call_held_since_the_last_read_is_new()
    {
        var calls = new[] { Call("ce_1"), Call("ce_2") };
        Assert.Empty(HoldToast.New(null, calls));
        Assert.Equal(["ce_2"], HoldToast.New(new HashSet<string> { "ce_1" }, calls).Select(c => c.KiffId));
        Assert.Empty(HoldToast.New(new HashSet<string> { "ce_1", "ce_2" }, calls));
    }
}

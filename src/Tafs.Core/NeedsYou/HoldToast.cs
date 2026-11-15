using System.Globalization;
using Tafs.Core.Words;

namespace Tafs.Core.NeedsYou;

/// <summary>A held call's notification: its two lines, and the hold it opens.</summary>
public sealed record ToastLines(string Title, string Body, string Hold);

/// <summary>
/// A held call's notification while tafs runs, worded as the iPad's <c>HoldNotification.words</c>: "Rae needs you" / "Refund
/// order 42, €150.00 · Over €50 each. Answer by 14:05. Nothing is sent until you approve." Clicking it opens Needs you at
/// that hold, as the iPad's <c>PushDelegate.open</c>.
/// </summary>
public static class HoldToast
{
    /// <summary>The notification's argument naming the hold it opens.</summary>
    public const string Argument = "hold";

    /// <summary>
    /// The two lines: the taf that needs the person, then what it asks (with the amount), why, and when to answer by, in this
    /// PC's time, which is the time every screen here shows. None for a call with no KIFF id: a sample is never notified.
    /// </summary>
    public static ToastLines? Lines(WordBook w, HeldCall call, TimeZoneInfo zone)
    {
        if (call.KiffId is not { } hold) return null;
        var what = string.Join(", ", new[] { call.Summary, call.Amount }.Where(s => !string.IsNullOrEmpty(s)));
        var by = TimeZoneInfo.ConvertTime(call.ExpiresAt, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
        return new(
            string.IsNullOrEmpty(call.Taf) ? w.Get("alert.needs_you") : w.Get("alert.taf_needs_you", ("taf", call.Taf)),
            w.Get("alert.answer_nothing_sent_until_you", ("what", what), ("why", call.Reason), ("zone", by)),
            hold);
    }

    /// <summary>
    /// The calls held since the last read: their ids weren't in <paramref name="seen"/>. A session's first read
    /// (<paramref name="seen"/> none) notifies nothing, so opening tafs doesn't announce what was already waiting; it's
    /// all in Needs you.
    /// </summary>
    public static IReadOnlyList<HeldCall> New(IReadOnlySet<string>? seen, IReadOnlyList<HeldCall> calls) =>
        seen is null ? [] : calls.Where(c => c.KiffId is { } id && !seen.Contains(id)).ToList();
}

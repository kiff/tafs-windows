using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tafs.Core.Words;

namespace Tafs.Core.NeedsYou;

/// <summary>A held call's notification: its lines (only the first while this PC is locked), and the hold it opens.</summary>
public sealed record ToastLines(string Title, string? Body, string Hold);

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
    /// While this PC is <paramref name="locked"/>, where Windows shows notifications on the lock screen, only "A taf needs
    /// you": no taf, amount, reason or deadline, as the iPad's alert with previews hidden and Android's while locked. It
    /// still opens that hold.
    /// </summary>
    public static ToastLines? Lines(WordBook w, HeldCall call, TimeZoneInfo zone, bool locked = false)
    {
        if (call.KiffId is not { } hold) return null;
        if (locked) return new(w.Get("alert.needs_you"), null, hold);
        var what = string.Join(", ", new[] { call.Summary, call.Amount }.Where(s => !string.IsNullOrEmpty(s)));
        var by = TimeZoneInfo.ConvertTime(call.ExpiresAt, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
        return new(
            Named(call) is { } taf ? w.Get("alert.taf_needs_you", ("taf", taf)) : w.Get("alert.needs_you"),
            w.Get("alert.answer_nothing_sent_until_you", ("what", what), ("why", call.Reason), ("zone", by)),
            hold);
    }

    /// <summary>
    /// The taf a notification names, as the iPad's <c>HoldNotification.fill</c>: a listed taf's name, "Coach" for Coach's own
    /// agent (<c>setup</c>), and none for an agent no taf has, which reads "A taf needs you". Not the card's "A taf", which
    /// is a name only on the card.
    /// </summary>
    public static string? Named(HeldCall call) => call.Listed || call.Avatar ? call.Taf : null;

    /// <summary>
    /// Whether this session is notified at all, as the iPad's <c>canAnswerHolds</c>: only someone who answers held calls
    /// (KIFF's <c>holds.answer</c> on <c>GET /v1/me</c>; none listed allows).
    /// </summary>
    public static bool Notifies(IReadOnlyList<string>? capabilities) => capabilities?.Contains("holds.answer") ?? true;

    /// <summary>
    /// A hold's notification tag: its id's SHA-256 in hex, 64 characters, Windows' limit, so two ids never share one.
    /// </summary>
    public static string Tag(string hold) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hold))).ToLowerInvariant();

    /// <summary>
    /// The holds whose notifications go, as the iPad's <c>removeAlerts(forHold:)</c>: notified before and no longer held,
    /// answered elsewhere or lapsed.
    /// </summary>
    public static IReadOnlyList<string> Gone(IReadOnlySet<string>? seen, IReadOnlyList<HeldCall> calls) =>
        seen is null ? [] : seen.Except(calls.Where(c => c.KiffId is not null).Select(c => c.KiffId!)).Order(StringComparer.Ordinal).ToList();

    /// <summary>How long tafs may sit minimised and still read held calls every minute.</summary>
    public static readonly TimeSpan MinimisedPause = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Whether the minute's read of held calls goes ahead: always while the window isn't minimised, and for
    /// <see cref="MinimisedPause"/> after it was; then it stops until the window comes back, which reads once.
    /// </summary>
    public static bool Reads(DateTimeOffset? minimisedSince, DateTimeOffset now) => minimisedSince is not { } since || now - since <= MinimisedPause;

    /// <summary>
    /// The calls held since the last read: their ids weren't in <paramref name="seen"/>. A session's first read
    /// (<paramref name="seen"/> none) notifies nothing, so opening tafs doesn't announce what was already waiting; it's
    /// all in Needs you.
    /// </summary>
    public static IReadOnlyList<HeldCall> New(IReadOnlySet<string>? seen, IReadOnlyList<HeldCall> calls) =>
        seen is null ? [] : calls.Where(c => c.KiffId is { } id && !seen.Contains(id)).ToList();
}

using System.Text.Json;
using Tafs.Core.Taf;

namespace Tafs.Core.Coach;

/// <summary>
/// A run that ended without an answer, as tafs cloud's conversation says it (<c>failed</c>, contracts/conversation-failed.json),
/// the same reading as Android's <c>RunFailed</c>: the run, its words for people (never the agent's), its code, and whether
/// sending the run's message again is safe (<see cref="Retry"/>, tafs cloud's rule: nothing the run did had an effect) with
/// its words for that button. The app decides neither.
/// </summary>
public sealed record RunFailed(string Run, string Words, string? Code, bool Retry, string? RetryLabel)
{
    /// <summary>The codes tafs cloud's <c>failed</c> carries: Try again only ever shows for one of these.</summary>
    public static readonly IReadOnlySet<string> RetryCodes =
        new HashSet<string>(StringComparer.Ordinal) { "ai_unavailable", "no_credits", "ai_consent_required", "timeout", "failed" };

    /// <summary>
    /// The conversation's <c>failed</c>: none without its run or its words. The label counts only with <c>retry</c> and
    /// when it has words.
    /// </summary>
    public static RunFailed? Read(JsonElement answer)
    {
        if (Lenient.Field(answer, "failed") is not { ValueKind: JsonValueKind.Object } f
            || Lenient.Text(f, "run") is not { Length: > 0 } run || Lenient.Text(f, "words")?.Trim() is not { Length: > 0 } words) return null;
        var retry = Lenient.Field(f, "retry") is { ValueKind: JsonValueKind.True };
        var label = retry && Lenient.Text(f, "retry_label")?.Trim() is { Length: > 0 } l ? l : null;
        return new(run, words, Lenient.Text(f, "code"), retry, label);
    }
}

/// <summary>Try again under a failed run's line: the person's own words for <see cref="Run"/>, sent again; the button's words, tafs cloud's.</summary>
public sealed record FailedRetry(string Run, string Text, string? Label)
{
    /// <summary>
    /// Try again under a failed run's line, or none, as Android's <c>failedRetry</c>: only when tafs cloud says so
    /// (<c>failed.retry</c>), for a code it's known to send, and when the run's own message (<c>&lt;run&gt;.in</c>, the person's)
    /// is in the chat as plain words. Not once it was pressed for this run (<paramref name="retried"/>), nor while the chat is
    /// sending or at work (<paramref name="busy"/>). A message with files, or a card's answer, isn't the same message sent again
    /// as words, so those get the line only.
    /// </summary>
    public static FailedRetry? Of(RunFailed? failed, IReadOnlyList<ChatMessage> messages, string? retried, bool busy)
    {
        if (failed is not { Retry: true } f || f.Code is not { } code || !RunFailed.RetryCodes.Contains(code) || retried == f.Run || busy) return null;
        var own = messages.LastOrDefault(m => m.From == ChatFrom.Owner && m.ServerId == f.Run + ".in");
        if (own is null || own.Files is { Count: > 0 } || own.Answers is not null || string.IsNullOrWhiteSpace(own.Text)) return null;
        return new(f.Run, own.Text, f.RetryLabel);
    }
}

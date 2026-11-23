using Tafs.Core.Coach;

namespace Tafs.Core.Taf;

/// <summary>
/// How often a thread (a taf's chat, Coach's) is read again while something happens in it: the scale review's finding 4
/// (kiff/tafs <c>docs/scale.md</c>). A Windows PC gets no push, so it polls, but only while the thread
/// moves: every 2 s for the first minute after its last change, then every 15 s, then every minute after 10 minutes, each
/// wait jittered ±20% so windows don't read in step. Thirty minutes with no change, it stops: opening the tab again, F5,
/// writing, or coming back to the window while the run still works reads again and starts over.
/// </summary>
public sealed class ChatFollow
{
    /// <summary>The wait while the thread has just changed: its answer is likely streaming in.</summary>
    public static readonly TimeSpan Fast = TimeSpan.FromSeconds(2);

    /// <summary>How long after its last change the thread is read at <see cref="Fast"/>.</summary>
    public static readonly TimeSpan FastFor = TimeSpan.FromMinutes(1);

    /// <summary>The wait once the thread has been quiet a minute.</summary>
    public static readonly TimeSpan Slow = TimeSpan.FromSeconds(15);

    /// <summary>After this long quiet, the wait is <see cref="Slowest"/>.</summary>
    public static readonly TimeSpan SlowFor = TimeSpan.FromMinutes(10);

    /// <summary>The wait for a thread quiet more than <see cref="SlowFor"/>.</summary>
    public static readonly TimeSpan Slowest = TimeSpan.FromMinutes(1);

    /// <summary>After this long quiet, nothing more is read until someone acts: the cap.</summary>
    public static readonly TimeSpan StopAfter = TimeSpan.FromMinutes(30);

    /// <summary>How far each wait is moved, either way, as a share of it.</summary>
    public const double Jitter = 0.2;

    DateTimeOffset? since;
    string? last;

    /// <summary>Nothing followed: the next read starts over at <see cref="Fast"/>.</summary>
    public void Reset() => (since, last) = (null, null);

    /// <summary>
    /// After a read whose thread is still moving (a run works, a message waits): the wait before the next read, or none to
    /// stop. <paramref name="signature"/> is the thread as read (<see cref="Signature"/>); a different one is a change.
    /// <paramref name="unit"/> is a random number in [0, 1) for the jitter.
    /// </summary>
    public TimeSpan? Next(string signature, DateTimeOffset now, double unit)
    {
        if (since is null || signature != last) (since, last) = (now, signature);
        var quiet = now - since.Value;
        if (quiet >= StopAfter) return null;
        var wait = quiet < FastFor ? Fast : quiet < SlowFor ? Slow : Slowest;
        return wait * (1 - Jitter + 2 * Jitter * Math.Clamp(unit, 0, 1));
    }

    /// <summary>
    /// What tells one read of a thread from the next: its messages (how many, the last one's time and length), its lines,
    /// the run at work, and how many sent messages still wait for it.
    /// </summary>
    public static string Signature(CoachThread? thread, int waiting)
    {
        if (thread is null) return $"none|{waiting}";
        var end = thread.Messages.Count > 0 ? thread.Messages[^1] : null;
        return $"{thread.Messages.Count}|{end?.At.ToUnixTimeMilliseconds()}|{end?.Text.Length}|{thread.Lines.Count}|{thread.WorkingRun ?? thread.Working}|{waiting}";
    }
}

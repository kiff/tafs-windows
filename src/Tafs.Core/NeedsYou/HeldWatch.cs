namespace Tafs.Core.NeedsYou;

/// <summary>
/// How a window reads KIFF's held calls on its own, between reads someone asked for (sign-in, F5, coming back): the scale
/// review's finding 6 (kiff/tafs <c>docs/scale.md</c>). A Windows PC gets no push for a hold, so the window still reads
/// on a timer, since a held call's notification depends on it. But each read is jittered so windows don't read in step,
/// and the account's capabilities (<c>GET /v1/me</c>, which change rarely) are read again at most every
/// <see cref="MeEvery"/>, not with every minute's read. That halves what the timer asks of KIFF.
/// </summary>
public static class HeldWatch
{
    /// <summary>The wait between timed reads, before jitter.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    /// <summary>How far each wait is moved, either way, as a share of it.</summary>
    public const double Jitter = 0.2;

    /// <summary>How long a timed read uses the capabilities already read before reading them again.</summary>
    public static readonly TimeSpan MeEvery = TimeSpan.FromMinutes(15);

    /// <summary>The wait before the next timed read; <paramref name="unit"/> is a random number in [0, 1).</summary>
    public static TimeSpan Wait(double unit) => Every * (1 - Jitter + 2 * Jitter * Math.Clamp(unit, 0, 1));

    /// <summary>
    /// Whether a read of held calls reads the capabilities too: always for a read someone asked for (<paramref name="timed"/>
    /// false), and for a timed one only when none were read yet this session, or the last read was
    /// <see cref="MeEvery"/> ago or more.
    /// </summary>
    public static bool ReadsMe(bool timed, DateTimeOffset? meReadAt, DateTimeOffset now) =>
        !timed || meReadAt is not { } at || now - at >= MeEvery;
}

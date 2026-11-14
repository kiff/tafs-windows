namespace Tafs.Core.Ci;

public sealed record DriftResult(bool Ok, TimeSpan Behind, int CommitsBehind, string Message);

/// <summary>
/// The drift check: the pin may not be more than 7 days or 20 commits behind the shared repository's main.
/// </summary>
public static class PinDrift
{
    public const int MaxCommitsBehind = 20;
    public static readonly TimeSpan MaxTimeBehind = TimeSpan.FromDays(7);

    /// <param name="pinCommitDate">When the pinned commit was made.</param>
    /// <param name="headCommitDate">When main's latest commit was made.</param>
    /// <param name="commitsBehind">How many commits main has beyond the pin.</param>
    public static DriftResult Evaluate(DateTimeOffset pinCommitDate, DateTimeOffset headCommitDate, int commitsBehind)
    {
        // A pin on main's tip is never behind, however old that tip is.
        if (commitsBehind <= 0)
            return new DriftResult(true, TimeSpan.Zero, 0, "pin is at main");

        var behind = headCommitDate - pinCommitDate;
        var problems = new List<string>();
        if (commitsBehind > MaxCommitsBehind)
            problems.Add($"{commitsBehind} commits behind (limit {MaxCommitsBehind})");
        if (behind > MaxTimeBehind)
            problems.Add($"{behind.TotalDays:0.#} days behind (limit {MaxTimeBehind.TotalDays:0})");

        return problems.Count == 0
            ? new DriftResult(true, behind, commitsBehind, $"{commitsBehind} commits, {behind.TotalDays:0.#} days behind: within limits")
            : new DriftResult(false, behind, commitsBehind, "pin has drifted: " + string.Join("; ", problems) + ". Bump shared-pin.json.");
    }
}

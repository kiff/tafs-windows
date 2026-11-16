using Tafs.Core.Home;

namespace Tafs.Core.Tests.Home;

/// <summary>Your tafs' order, as the iPad's listedTafs, byRecent, inOrder, waitingFirst and tafSections.</summary>
[Part("home")]
public class ListOrderTests
{
    static readonly IReadOnlyList<string> Tafs = ["a", "b", "c", "d"];
    static string Id(string x) => x;
    static DateTimeOffset At(int minute) => new(2026, 11, 16, 9, minute, 0, TimeSpan.Zero);

    [Fact]
    public void Waiting_first_then_the_latest_used_then_tafs_cloud_s_order()
    {
        var latest = new Dictionary<string, DateTimeOffset> { ["c"] = At(5), ["b"] = At(9) };
        Assert.Equal(["d", "b", "c", "a"], ListOrder.ByRecent(Tafs, Id, latest, new HashSet<string> { "d" }));
        Assert.Equal(["a", "b", "c", "d"], ListOrder.ByRecent(Tafs, Id, new Dictionary<string, DateTimeOffset>(), new HashSet<string>()));
    }

    /// <summary>Settled, the list doesn't move on a read; a new taf comes after; one newly waiting goes to the top at once.</summary>
    [Fact]
    public void A_settled_order_holds_and_only_a_newly_waiting_taf_moves()
    {
        IReadOnlyList<string> order = ["c", "a", "b", "d"];
        var latest = new Dictionary<string, DateTimeOffset> { ["d"] = At(30) };
        Assert.Equal(["c", "a", "b", "d"], ListOrder.Listed(Tafs, Id, order, latest, new HashSet<string>(), new HashSet<string>()));
        Assert.Equal(["c", "a", "b", "d", "e"], ListOrder.Listed([.. Tafs, "e"], Id, order, latest, new HashSet<string>(), new HashSet<string>()));
        Assert.Equal(["b", "c", "a", "d"], ListOrder.Listed(Tafs, Id, order, latest, new HashSet<string> { "b" }, new HashSet<string>()));
        // Already waiting when it was settled: placed by the order, not moved again.
        Assert.Equal(["c", "a", "b", "d"], ListOrder.Listed(Tafs, Id, order, latest, new HashSet<string> { "b" }, new HashSet<string> { "b" }));
        // Nothing settled yet: by recent use.
        Assert.Equal(["d", "a", "b", "c"], ListOrder.Listed(Tafs, Id, [], latest, new HashSet<string>(), new HashSet<string>()));
    }

    [Fact]
    public void Shared_with_you_is_only_what_KIFF_says_isn_t_theirs_to_manage()
    {
        var manages = new Dictionary<string, bool> { ["a"] = true, ["b"] = false };
        var (own, shared) = ListOrder.Split(Tafs, x => manages.TryGetValue(x, out var m) ? m : null);
        Assert.Equal(["a", "c", "d"], own);
        Assert.Equal(["b"], shared);
    }
}

namespace Tafs.Core.Home;

/// <summary>
/// The order of Your tafs, as the iPad's <c>listedTafs</c>, <c>byRecent</c>, <c>inOrder</c> and <c>waitingFirst</c>: a taf
/// waiting on the person first, then the one used most recently (its latest call in the Record), then tafs cloud's order.
/// The order is settled when the list's tafs change or the app comes back, never on each read, so it doesn't move under
/// the person; a taf newly waiting still goes to the top at once.
/// </summary>
public static class ListOrder
{
    /// <summary>Waiting first, then the latest used, then the order given, as the iPad's <c>byRecent</c>.</summary>
    public static IReadOnlyList<T> ByRecent<T>(IReadOnlyList<T> items, Func<T, string> id, IReadOnlyDictionary<string, DateTimeOffset> latest,
        IReadOnlySet<string> waiting) =>
        items.Select((item, i) => (item, i))
            .OrderBy(x => waiting.Contains(id(x.item)) ? 0 : 1)
            .ThenByDescending(x => latest.TryGetValue(id(x.item), out var at) ? at : DateTimeOffset.MinValue)
            .ThenBy(x => x.i)
            .Select(x => x.item).ToList();

    /// <summary>The items in a settled order; one the order doesn't know yet comes after, as <paramref name="fallback"/> has it.</summary>
    public static IReadOnlyList<T> InOrder<T>(IReadOnlyList<T> items, Func<T, string> id, IReadOnlyList<string> order, IReadOnlyList<T> fallback)
    {
        if (order.Count == 0) return fallback;
        var place = new Dictionary<string, int>();
        for (var i = 0; i < order.Count; i++) place.TryAdd(order[i], i);
        return [.. items.Where(x => place.ContainsKey(id(x))).OrderBy(x => place[id(x)]), .. fallback.Where(x => !place.ContainsKey(id(x)))];
    }

    /// <summary>The ones newly waiting first, each part in the order it had.</summary>
    public static IReadOnlyList<T> WaitingFirst<T>(IReadOnlyList<T> items, Func<T, string> id, IReadOnlySet<string> waiting) =>
        [.. items.Where(x => waiting.Contains(id(x))), .. items.Where(x => !waiting.Contains(id(x)))];

    /// <summary>
    /// The list as shown: in the settled order, those that came to wait since it was settled first, as the iPad's
    /// <c>listedTafs</c>.
    /// </summary>
    public static IReadOnlyList<T> Listed<T>(IReadOnlyList<T> items, Func<T, string> id, IReadOnlyList<string> order,
        IReadOnlyDictionary<string, DateTimeOffset> latest, IReadOnlySet<string> waiting, IReadOnlySet<string> waitingWhenSettled)
    {
        var settled = InOrder(items, id, order, ByRecent(items, id, latest, waiting));
        return WaitingFirst(settled, id, waiting.Where(x => !waitingWhenSettled.Contains(x)).ToHashSet());
    }

    /// <summary>
    /// The list in two, as the iPad's <c>tafSections</c>: the tafs this person manages, then those shared with them to use, each
    /// in the list's order. Which is KIFF's answer; a taf whose answer isn't in yet counts as theirs, so nothing moves into
    /// "Shared with you" and back.
    /// </summary>
    public static (IReadOnlyList<T> Own, IReadOnlyList<T> Shared) Split<T>(IReadOnlyList<T> items, Func<T, bool?> manages) =>
        (items.Where(x => manages(x) != false).ToList(), items.Where(x => manages(x) == false).ToList());
}

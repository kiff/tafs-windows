using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Home;

/// <summary>
/// One of the account's labels, as Home sends it (kiff/tafs <c>docs/labels.md</c>): its id and name, its colour token, how many
/// listed tafs carry it, how many of those wait for the person (the amber dot), and Home's headline filtered to it.
/// </summary>
public sealed record HomeLabel(string Id, string Name, string Color, int Count, int Waiting, string? Headline);

/// <summary>The account's labels in the order to show them, and tafs cloud's word for the first chip ("All").</summary>
public sealed record HomeLabels(IReadOnlyList<HomeLabel> Labels, string? All)
{
    public static readonly HomeLabels None = new([], null);
}

/// <summary>
/// Labels on the tafs list, as the iPad's <c>LabelList</c>, <c>LabelChips</c> and <c>AppModel.filterList</c>: chips to filter
/// the Overview by, and each taf's own under its name. The apps never change a label; they only filter.
/// </summary>
public static class LabelChips
{
    /// <summary>
    /// The account's labels from Home: <c>labels: [{id, name, color, count, waiting, headline: {words}}]</c> and
    /// <c>labels_words.all</c>. Read one by one: a label with no id or name, or of another shape, is left out, and
    /// <c>waiting</c> sent as yes/no reads as one or none. A list of another shape reads as none; Home still reads.
    /// </summary>
    public static HomeLabels Read(JsonElement answer) => new(
        Lenient.Array(answer, "labels").Where(l => l.ValueKind == JsonValueKind.Object
                && Lenient.Text(l, "id") is { Length: > 0 } && Lenient.Text(l, "name") is { Length: > 0 })
            .Select(l => new HomeLabel(Lenient.Text(l, "id")!, Lenient.Text(l, "name")!, Lenient.Text(l, "color") ?? "",
                Lenient.Int(l, "count") ?? 0,
                Lenient.Int(l, "waiting") ?? (Lenient.Bool(l, "waiting") ? 1 : 0),
                Lenient.Field(l, "headline") is { } h && Lenient.Text(h, "words") is { Length: > 0 } words ? words : null))
            .ToList(),
        Lenient.Field(answer, "labels_words") is { } lw && Lenient.Text(lw, "all") is { Length: > 0 } all ? all : null);

    /// <summary>A taf's label ids (<c>labels: [id]</c>); anything but text is left out.</summary>
    public static IReadOnlyList<string> Ids(JsonElement taf) =>
        Lenient.Array(taf, "labels").Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!).ToList();

    /// <summary>The first chip: tafs cloud's word, else Home's own "All" (<c>home.labels_all</c>, as the iPad's).</summary>
    public static string All(WordBook w, HomeLabels labels) => labels.All ?? w.Get("home.labels_all");

    /// <summary>A taf's labels, in the account's order; an id the account doesn't list is left out.</summary>
    public static IReadOnlyList<HomeLabel> Of(IReadOnlyList<string> ids, HomeLabels labels) =>
        labels.Labels.Where(l => ids.Contains(l.Id)).ToList();

    /// <summary>
    /// The chosen label, if the account still has it: a label that's gone goes back to All, as the iPad's
    /// <c>filterList</c>.
    /// </summary>
    public static string? Chosen(string? label, HomeLabels labels) =>
        label is not null && labels.Labels.Any(l => l.Id == label) ? label : null;

    /// <summary>
    /// The tafs a chip shows: every one on All, else those carrying it, in Home's order. Filtering never reorders, and a
    /// label that's gone shows them all.
    /// </summary>
    public static IReadOnlyList<T> Filtered<T>(IReadOnlyList<T> tafs, Func<T, IReadOnlyList<string>?> labelsOf, string? label,
        HomeLabels labels) =>
        Chosen(label, labels) is { } chosen ? tafs.Where(t => labelsOf(t)?.Contains(chosen) == true).ToList() : tafs;

    /// <summary>The row of chips after All: two labels, then "+N" for the rest until it's clicked, which shows them all.</summary>
    public static (IReadOnlyList<HomeLabel> Shown, int Hidden) Row(HomeLabels labels, bool expanded)
    {
        var shown = expanded ? labels.Labels : labels.Labels.Take(2).ToList();
        return (shown, labels.Labels.Count - shown.Count);
    }

    /// <summary>A taf's own chips under its name: two names, then "+N" (a taf carries at most three).</summary>
    public static IReadOnlyList<string> TafChips(IReadOnlyList<HomeLabel> of) =>
        of.Count > 2 ? [of[0].Name, of[1].Name, $"+{of.Count - 2}"] : of.Select(l => l.Name).ToList();
}

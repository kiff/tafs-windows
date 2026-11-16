using System.Globalization;
using System.Text.Json;

namespace Tafs.Core.Home;

/// <summary>A colour as tafs cloud sends it, <c>#RRGGBB</c> for light and for dark, as the iPad's <c>Recipes.Tint</c>.</summary>
public sealed record RecipeTint(uint Light, uint Dark)
{
    /// <summary><c>#RRGGBB</c>, and only that.</summary>
    public static uint? Hex(string? s) =>
        s is { Length: 7 } && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit)
            ? uint.Parse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;
}

/// <summary>A job's category: its title, and its colour where tafs cloud sends one it can read (else none, never a guess).</summary>
public sealed record RecipeCategory(string Id, string Title, RecipeTint? Color);

/// <summary>An app a job uses, by tafs cloud's name, with its logo's path on tafs cloud when it has one.</summary>
public sealed record RecipeApp(string Name, string? Logo);

/// <summary>
/// One ready-made job, as the iPad's <c>Recipes.Recipe</c>: what leads it (the problem it ends, else its title), its line,
/// what it costs today, the Card it would have, its apps, and tafs cloud's line for a passed proof run and what it reported.
/// </summary>
public sealed record Recipe(string Id, string Title, string Category, string Line, string Say, string Headline, string Today,
    IReadOnlyList<string> Allows, IReadOnlyList<string> Holds, IReadOnlyList<string> Never, IReadOnlyList<RecipeApp> Apps,
    bool Connected, bool Running, string Proved, string Sample)
{
    public string Lead => Headline.Trim().Length == 0 ? Title : Headline;
}

/// <summary>The job sheet's words: its zones' titles, and its button's.</summary>
public sealed record RecipeLabels(string Allows, string Holds, string Never, string Hand);

/// <summary>One zone of a job's Card on its sheet: its title and its lines.</summary>
public sealed record RecipeZone(string Id, string Title, IReadOnlyList<string> Lines);

/// <summary>The gallery's own words, as the iPad's <c>Gallery.Words</c>.</summary>
public sealed record GalleryWords(string Title, string All, string OwnWords, string OwnWordsLine, string Explore, string SeeAll,
    string TryIt, string Sample);

/// <summary>
/// Every ready-made job, as tafs cloud sends them (<c>GET /v1/recipes/gallery</c>), as the iPad's <c>TafsCloud.Gallery</c>:
/// the jobs by category, the strip under the tafs list, and its words. tafs cloud decides what shows and in what order;
/// the app draws it, with no words of its own.
/// </summary>
public sealed record Gallery(IReadOnlyList<RecipeCategory> Categories, IReadOnlyList<Recipe> Recipes, bool StripShown,
    string StripTitle, IReadOnlyList<string> StripIds, GalleryWords Words, RecipeLabels? Labels)
{
    public const string Path = "/v1/recipes/gallery";

    /// <summary>
    /// The gallery, read leniently as the iPad's: a category, job or app it can't read is left out, never the gallery with
    /// it; a colour it can't read is none. Null for an answer that isn't an object.
    /// </summary>
    public static Gallery? Read(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object) return null;
        var categories = Items(a, "categories").Select(c => T(c, "id") is { } id && T(c, "title") is { } title
            ? new RecipeCategory(id, title, Obj(c, "color") is { } t && RecipeTint.Hex(T(t, "light")) is { } l && RecipeTint.Hex(T(t, "dark")) is { } d
                ? new RecipeTint(l, d) : null) : null).OfType<RecipeCategory>().ToList();
        var recipes = Items(a, "recipes").Select(RecipeOf).OfType<Recipe>().ToList();
        var strip = Obj(a, "strip");
        var w = Obj(a, "words");
        string W(string name) => w is { } o ? T(o, name, trim: false) ?? "" : "";
        var labels = Obj(a, "labels") is { } l2
            ? new RecipeLabels(T(l2, "allows", false) ?? "", T(l2, "holds", false) ?? "", T(l2, "never", false) ?? "", T(l2, "hand", false) ?? "") : null;
        return new(categories, recipes, strip is { } s && B(s, "shown"), strip is { } s2 ? T(s2, "title", false) ?? "" : "",
            strip is { } s3 ? Strings(s3, "ids") : [],
            new(W("title"), W("all"), W("own_words"), W("own_words_line"), W("explore"), W("see_all"), W("try"), W("sample")), labels);
    }

    static Recipe? RecipeOf(JsonElement r) =>
        T(r, "id", false) is { } id && T(r, "title", false) is { } title
            ? new(id, title, T(r, "category", false) ?? "", T(r, "line", false) ?? "", T(r, "say", false) ?? "", T(r, "headline", false) ?? "",
                T(r, "today", false) ?? "",
                Obj(r, "card") is { } c ? Strings(c, "allows") : [], Obj(r, "card") is { } c2 ? Strings(c2, "holds") : [],
                Obj(r, "card") is { } c3 ? Strings(c3, "never") : [],
                Items(r, "apps").Select(p => T(p, "name", false) is { } name ? new RecipeApp(name, T(p, "logo", false)) : null).OfType<RecipeApp>().ToList(),
                B(r, "connected"), B(r, "running"), T(r, "proved") ?? "", T(r, "sample") ?? "")
            : null;

    /// <summary>Home's row that opens the jobs to browse ("Explore jobs"); none when tafs cloud sends no words or no job.</summary>
    public string? Explore => Recipes.Count == 0 ? null : Said(Words.Explore);

    /// <summary>The strip's jobs, in tafs cloud's order, an id it doesn't list skipped; none when it isn't shown.</summary>
    public IReadOnlyList<Recipe> StripRecipes =>
        StripShown ? StripIds.Select(id => Recipes.FirstOrDefault(r => r.Id == id)).OfType<Recipe>().ToList() : [];

    /// <summary>The row under the strip that opens every job: "See all jobs", else "All jobs"; none when the strip shows them all.</summary>
    public string? SeeAll => Recipes.Count > StripRecipes.Count ? Said(Words.SeeAll) ?? Said(Words.All) : null;

    /// <summary>The gallery's title: browsing, "Explore jobs" where tafs cloud sends it; else its title.</summary>
    public string Title(bool browse) => (browse ? Said(Words.Explore) : null) ?? Words.Title;

    /// <summary>A job's one button: browsing, "Try it with Coach"; else the sheet's own (<c>labels.hand</c>). None, no button.</summary>
    public string? HandWords(bool browse) => (browse ? Said(Words.TryIt) : null) ?? Said(Labels?.Hand);

    /// <summary>The label over a job's sample; none when tafs cloud sends none.</summary>
    public string? SampleLabel => Said(Words.Sample);

    /// <summary>The category a job is in, for its colour.</summary>
    public RecipeCategory? CategoryOf(Recipe r) => Categories.FirstOrDefault(c => c.Id == r.Category);

    /// <summary>Each category in tafs cloud's order with its jobs; a category with none isn't shown.</summary>
    public IReadOnlyList<(RecipeCategory Category, IReadOnlyList<Recipe> Recipes)> ByCategory =>
        Categories.Select(c => (c, (IReadOnlyList<Recipe>)Recipes.Where(r => r.Category == c.Id).ToList())).Where(g => g.Item2.Count > 0).ToList();

    /// <summary>The categories listed, narrowed to the one picked; nothing picked, or one no longer listed, is all of them.</summary>
    public IReadOnlyList<(RecipeCategory Category, IReadOnlyList<Recipe> Recipes)> Groups(string? picked)
    {
        var all = ByCategory;
        return picked is not null && all.Any(g => g.Category.Id == picked) ? all.Where(g => g.Category.Id == picked).ToList() : all;
    }

    /// <summary>A job's zones, in order (does on its own, asks first, never does), titled by the labels; an empty one isn't one.</summary>
    public IReadOnlyList<RecipeZone> Zones(Recipe r)
    {
        var l = Labels ?? new RecipeLabels("", "", "", "");
        return new[] { new RecipeZone("allows", l.Allows, r.Allows), new RecipeZone("holds", l.Holds, r.Holds), new RecipeZone("never", l.Never, r.Never) }
            .Where(z => z.Lines.Count > 0).ToList();
    }

    /// <summary>
    /// Where an app's logo loads from: only a path tafs cloud sent, <c>/v1/logos/</c> and 32 lowercase hex, on tafs cloud
    /// itself, as the iPad's <c>logoURL</c>. Anything else loads nothing, so no one but tafs cloud sees this PC's address.
    /// </summary>
    public static bool IsLogo(string? path) =>
        path is { } p && p.StartsWith("/v1/logos/", StringComparison.Ordinal) && p.Length == "/v1/logos/".Length + 32
        && p["/v1/logos/".Length..].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    static string? Said(string? words) => words?.Trim() is { Length: > 0 } w ? w : null;

    static string? T(JsonElement o, string name, bool trim = true) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (trim ? v.GetString()!.Trim() : v.GetString()!) is { Length: > 0 } t ? t : null : null;

    static bool B(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    static JsonElement? Obj(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    static IEnumerable<JsonElement> Items(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object) : [];

    static IReadOnlyList<string> Strings(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!).ToList() : [];
}

namespace Tafs.Core.Words;

/// <summary>Which shipped language the app speaks, from the person's Windows languages, in their order.</summary>
public static class AppLanguage
{
    /// <param name="preferred">The person's languages, most wanted first ("fr-FR", "pt-BR", "en-GB").</param>
    /// <param name="shipped">The languages the app ships (languages.json).</param>
    public static string Pick(IEnumerable<string> preferred, IReadOnlyCollection<string> shipped)
    {
        foreach (var tag in preferred)
            if (Shipped(tag, shipped) is { } language) return language;
        return "en";
    }

    /// <summary>
    /// A tag as tafs ships it: its regional variant when tafs ships that ("pt-BR"), else its language ("pt-PT" reads "pt");
    /// none when tafs ships neither.
    /// </summary>
    static string? Shipped(string? tag, IReadOnlyCollection<string> shipped)
    {
        var parts = (tag ?? "").Trim().Split('-', '_');
        var language = parts[0].ToLowerInvariant();
        if (language.Length == 0) return null;
        if (parts.Length > 1 && parts[1].Length == 2)
        {
            var variant = language + "-" + parts[1].ToUpperInvariant();
            if (shipped.Contains(variant)) return variant;
        }
        return shipped.Contains(language) ? language : null;
    }

    /// <summary>
    /// tafs's languages in the Terms table's order, each named in itself, as Android's <c>AppLanguage.name</c>; a regional
    /// variant is named with its region ("Português (Brasil)"). The in-app picker's list, after this PC's own language.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Named =
    [
        ("en", "English"), ("de", "Deutsch"), ("fr", "Français"), ("es", "Español"),
        ("it", "Italiano"), ("pt", "Português"), ("pt-BR", "Português (Brasil)"), ("nl", "Nederlands"), ("pl", "Polski"),
        ("ko", "한국어"),
    ];

    /// <summary>
    /// The person's kept choice as the app keeps it ("fr", or "fr-FR" read as its language), if tafs ships it; none (this PC's
    /// own language) for anything else, so an old or odd choice never stops the app.
    /// </summary>
    public static string? Kept(string? choice, IReadOnlyCollection<string> shipped) => Shipped(choice, shipped);

    /// <summary>
    /// The language the app speaks: <c>--lang</c> for a screenshot, else the person's kept choice, else the first shipped
    /// language of this PC's, else English.
    /// </summary>
    public static string Resolve(string? option, string? kept, IEnumerable<string> preferred, IReadOnlyCollection<string> shipped) =>
        option is not null ? Pick([option], shipped) : Kept(kept, shipped) ?? Pick(preferred, shipped);
}

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
        var rest = parts.Skip(1).Select(p => p.ToLowerInvariant()).ToList();
        switch (language)
        {
            // Chinese: tafs speaks Traditional only ("zh-Hant", "zh-TW", "zh-HK", "zh-MO"); Simplified isn't spoken.
            case "zh":
                var traditional = rest.Contains("hant") || (!rest.Contains("hans") && rest.Any(p => p is "tw" or "hk" or "mo"));
                return traditional && shipped.Contains("zh-Hant") ? "zh-Hant" : null;
            // Serbian ships in Latin script: a Cyrillic tag ("sr-RS", "sr-Cyrl") isn't the language tafs ships.
            case "sr":
                return rest.Contains("latn") && shipped.Contains("sr-Latn") ? "sr-Latn" : null;
        }
        if (parts.Length > 1 && parts[1].Length == 2)
        {
            var variant = language + "-" + parts[1].ToUpperInvariant();
            if (shipped.Contains(variant)) return variant;
        }
        return shipped.Contains(language) ? language : null;
    }

    /// <summary>
    /// tafs's languages in the Terms table's order, each named in itself, as Android's <c>AppLanguage.name</c>; a regional
    /// variant is named with its region ("Português (Brasil)"), last. Every language tafs has words for, shipped or not;
    /// the picker shows those tafs ships (<see cref="Picker"/>).
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Named =
    [
        ("en", "English"), ("de", "Deutsch"), ("fr", "Français"), ("es", "Español"), ("it", "Italiano"), ("pt", "Português"),
        ("nl", "Nederlands"), ("pl", "Polski"), ("ko", "한국어"), ("ja", "日本語"), ("et", "Eesti"), ("lt", "Lietuvių"),
        ("lv", "Latviešu"), ("cs", "Čeština"), ("sk", "Slovenčina"), ("sl", "Slovenščina"), ("hu", "Magyar"), ("ro", "Română"),
        ("bg", "Български"), ("hr", "Hrvatski"), ("tr", "Türkçe"), ("id", "Indonesia"), ("vi", "Tiếng Việt"), ("th", "ไทย"),
        ("zh-Hant", "中文（繁體）"), ("ar", "العربية"), ("sr-Latn", "Srpski (latinica)"), ("sq", "Shqip"), ("ka", "ქართული"),
        ("hy", "Հայերեն"), ("az", "Azərbaycan"), ("bn", "বাংলা"), ("he", "עברית"), ("ca", "Català"), ("mk", "Македонски"),
        ("mn", "Монгол"), ("ne", "नेपाली"), ("km", "ខ្មែរ"), ("lo", "ລາວ"), ("tk", "Türkmen dili"), ("hi", "हिन्दी"), ("ur", "اردو"), ("el", "Ελληνικά"), ("pt-BR", "Português (Brasil)"),
    ];

    /// <summary>The in-app picker's rows, after this PC's own language: <see cref="Named"/>'s languages tafs ships, in that order.</summary>
    public static IReadOnlyList<(string Code, string Name)> Picker(IReadOnlyCollection<string> shipped) =>
        Named.Where(l => shipped.Contains(l.Code)).ToList();

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

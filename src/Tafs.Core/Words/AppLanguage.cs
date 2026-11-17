namespace Tafs.Core.Words;

/// <summary>Which shipped language the app speaks, from the person's Windows languages, in their order.</summary>
public static class AppLanguage
{
    /// <param name="preferred">The person's languages, most wanted first ("fr-FR", "pt-BR", "en-GB").</param>
    /// <param name="shipped">The languages the app ships (languages.json).</param>
    public static string Pick(IEnumerable<string> preferred, IReadOnlyCollection<string> shipped)
    {
        foreach (var tag in preferred)
        {
            var language = tag.Split('-', '_')[0].ToLowerInvariant();
            if (shipped.Contains(language)) return language;
        }
        return "en";
    }

    /// <summary>
    /// tafs's languages in the Terms table's order, each named in itself, as Android's <c>AppLanguage.name</c>:
    /// the in-app picker's list, after this PC's own language.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Named =
    [
        ("en", "English"), ("de", "Deutsch"), ("fr", "Français"), ("es", "Español"),
        ("it", "Italiano"), ("pt", "Português"), ("nl", "Nederlands"), ("pl", "Polski"),
    ];

    /// <summary>
    /// The person's kept choice as the app keeps it ("fr", or "fr-FR" read as its language), if tafs ships it; none (this PC's
    /// own language) for anything else, so an old or odd choice never stops the app.
    /// </summary>
    public static string? Kept(string? choice, IReadOnlyCollection<string> shipped)
    {
        var language = choice?.Trim().Split('-', '_')[0].ToLowerInvariant();
        return !string.IsNullOrEmpty(language) && shipped.Contains(language) ? language : null;
    }

    /// <summary>
    /// The language the app speaks: <c>--lang</c> for a screenshot, else the person's kept choice, else the first shipped
    /// language of this PC's, else English.
    /// </summary>
    public static string Resolve(string? option, string? kept, IEnumerable<string> preferred, IReadOnlyCollection<string> shipped) =>
        option is not null ? Pick([option], shipped) : Kept(kept, shipped) ?? Pick(preferred, shipped);
}

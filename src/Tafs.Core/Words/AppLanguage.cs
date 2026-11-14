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
}

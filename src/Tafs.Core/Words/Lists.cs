namespace Tafs.Core.Words;

/// <summary>Names joined in the person's language as the iPad's ListFormatter joins them: CLDR's standard "and" list.</summary>
public static class Lists
{
    public static string And(string language, IReadOnlyList<string> names)
    {
        if (names.Count == 0) return "";
        if (names.Count == 1) return names[0];
        var head = string.Join(", ", names.Take(names.Count - 1));
        var last = names[^1];
        // English puts a comma before "and" from three on; Spanish says "e" before an "i" sound.
        var and = language switch
        {
            "de" => "und",
            "fr" => "et",
            "es" => SaysI(last) ? "e" : "y",
            "it" or "pt" => "e",
            "nl" => "en",
            "pl" => "i",
            _ => names.Count > 2 ? ", and" : "and",
        };
        return and.StartsWith(',') ? $"{head}{and} {last}" : $"{head} {and} {last}";
    }

    /// <summary>"Iberia", "Hilton": an "i" sound; but "hie", "hia", "hio" and "hiu" begin with a "y" sound ("y hielo").</summary>
    static bool SaysI(string word)
    {
        var w = word.ToLowerInvariant();
        return w.StartsWith('i') || (w.StartsWith("hi") && !(w.Length > 2 && "aeou".Contains(w[2])));
    }
}

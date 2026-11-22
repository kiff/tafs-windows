namespace Tafs.Core.Words;

/// <summary>
/// Names joined in the person's language as the iPad's ListFormatter joins them: CLDR's standard "and" list
/// (<see cref="Cldr.And"/>, generated), with the two rules ICU applies in code rather than data: Spanish says "e" before
/// an "i" sound, and Hebrew writes "ו-" before a word that isn't in Hebrew letters.
/// </summary>
public static class Lists
{
    public static string And(string language, IReadOnlyList<string> names)
    {
        if (names.Count == 0) return "";
        if (names.Count == 1) return names[0];
        var p = Cldr.And.TryGetValue(language, out var found) ? found : Cldr.And["en"];
        string Join(string pattern, string a, string b)
        {
            if (language == "es" && SaysI(b)) pattern = pattern.Replace(" y {1}", " e {1}");
            if (language == "he" && b.Length > 0 && !IsHebrew(b[0])) pattern = pattern.Replace("ו{1}", "ו-{1}");
            return pattern.Replace("{0}", a).Replace("{1}", b);
        }
        if (names.Count == 2) return Join(p.Two, names[0], names[1]);
        // CLDR builds from the end: end(…, last), then middle and start outward.
        var text = Join(p.End, names[^2], names[^1]);
        for (var i = names.Count - 3; i >= 1; i--) text = Join(p.Middle, names[i], text);
        return Join(p.Start, names[0], text);
    }

    /// <summary>"Iberia", "Hilton": an "i" sound; but "hie", "hia", "hio" and "hiu" begin with a "y" sound ("y hielo").</summary>
    static bool SaysI(string word)
    {
        var w = word.ToLowerInvariant();
        return w.StartsWith('i') || (w.StartsWith("hi") && !(w.Length > 2 && "aeou".Contains(w[2])));
    }

    static bool IsHebrew(char c) => c is >= '֐' and <= '׿';
}

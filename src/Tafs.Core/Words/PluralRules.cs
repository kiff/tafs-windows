namespace Tafs.Core.Words;

/// <summary>
/// The plural form (CLDR cardinal, whole numbers) a count takes in each language the app ships: zero, one, two, few,
/// many or other. A line gives the forms its language needs, and always `other`. The forms per language are those of
/// kiff/tafs `app/strings/generate.py`'s <c>PLURALS</c>; the rules are CLDR's for whole numbers (v = 0), so forms only
/// fractions take (Czech's "many", Lithuanian's "many") never appear.
/// </summary>
public static class PluralRules
{
    public static string Form(string language, long count)
    {
        var n = Math.Abs(count);
        var n10 = n % 10;
        var n100 = n % 100;
        switch (language)
        {
            // pt is European Portuguese (pt-PT), where only 1 is "one"; Brazil's rule would make 0 "one" too.
            case "en": case "de": case "es": case "it": case "nl": case "pt": case "ca": case "et":
            case "el": case "hu": case "bg": case "tr": case "az": case "sq": case "ka": case "mn": case "ne":
                return n == 1 ? "one" : "other";
            // 0 and 1 are "one": Brazilian Portuguese (CLDR pt), French, Armenian, Bengali.
            case "pt-BR": case "fr": case "hy": case "bn":
                return n <= 1 ? "one" : "other";
            // One form for every number.
            case "ko": case "ja": case "id": case "vi": case "th": case "zh-Hant": case "km": case "lo":
                return "other";
            case "pl":
                if (n == 1) return "one";
                return n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? "few" : "many";
            case "cs": case "sk":
                return n == 1 ? "one" : n is >= 2 and <= 4 ? "few" : "other";
            case "lt":
                if (n100 is >= 11 and <= 19) return "other";
                return n10 == 1 ? "one" : n10 >= 2 ? "few" : "other";
            case "lv":
                if (n10 == 0 || n100 is >= 11 and <= 19) return "zero";
                return n10 == 1 ? "one" : "other";
            case "sl":
                return n100 switch { 1 => "one", 2 => "two", 3 or 4 => "few", _ => "other" };
            case "ro":
                if (n == 1) return "one";
                return n == 0 || n100 is >= 1 and <= 19 ? "few" : "other";
            case "hr": case "sr-Latn":
                if (n10 == 1 && n100 != 11) return "one";
                return n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? "few" : "other";
            case "mk":
                return n10 == 1 && n100 != 11 ? "one" : "other";
            // Hebrew's "many" left CLDR in 42.
            case "he":
                return n switch { 1 => "one", 2 => "two", _ => "other" };
            case "ar":
                if (n is 0) return "zero";
                if (n is 1) return "one";
                if (n is 2) return "two";
                return n100 is >= 3 and <= 10 ? "few" : n100 >= 11 ? "many" : "other";
            default:
                throw new ArgumentException($"no plural rule for language '{language}'", nameof(language));
        }
    }
}

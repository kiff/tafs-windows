namespace Tafs.Core.Words;

/// <summary>
/// The plural form (CLDR cardinal, whole numbers) a count takes in each language the app ships:
/// one, few, many or other. A line gives the forms its language needs, and always `other`.
/// </summary>
public static class PluralRules
{
    public static string Form(string language, long count)
    {
        var n = Math.Abs(count);
        switch (language)
        {
            // pt is European Portuguese (pt-PT), where only 1 is "one"; Brazil's rule would make 0 "one" too.
            case "en": case "de": case "es": case "it": case "nl": case "pt":
                return n == 1 ? "one" : "other";
            // Brazilian Portuguese (pt-BR): 0 and 1 are "one" (CLDR pt).
            case "pt-BR":
                return n <= 1 ? "one" : "other";
            // Korean and Japanese have one form for every number.
            case "ko": case "ja":
                return "other";
            case "fr":
                return n <= 1 ? "one" : "other";
            case "pl":
                if (n == 1) return "one";
                var last = n % 10;
                var lastTwo = n % 100;
                return last is >= 2 and <= 4 && lastTwo is < 12 or > 14 ? "few" : "many";
            default:
                throw new ArgumentException($"no plural rule for language '{language}'", nameof(language));
        }
    }
}

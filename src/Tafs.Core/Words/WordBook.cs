using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tafs.Core.Words;

/// <summary>
/// The app's words in one language, read from the shared repository's `app/strings/&lt;lang&gt;/*.json`
/// at the pinned commit. Nothing is translated here: a missing key is an error, never English.
/// </summary>
public sealed partial class WordBook
{
    /// <summary>A name's possessive by the language's rule: "Refunds'", "Rae's". Other languages keep the name.</summary>
    static readonly Dictionary<string, (string Ends, string After, string Other)> Possessive = new()
    {
        ["en"] = ("s", "'", "'s"),
        ["de"] = ("sßxz", "'", "s"),
    };

    /// <summary>
    /// "This name" by the language's rule (`THIS` in the shared generate.py): French "ce Mac", "cet appareil".
    /// Right for the device kinds the apps name: every h is taken as mute and y as a vowel. Other languages keep the name.
    /// </summary>
    static readonly Dictionary<string, (string Starts, string Before, string Other)> This = new()
    {
        ["fr"] = ("aeiouyhàâäéèêëîïôöùûüœæ", "cet ", "ce "),
    };

    /// <summary>`{name}`, or `{name|form}`: `possessive`, `this`, or `This` to open a sentence.</summary>
    [GeneratedRegex(@"\{(\w+)(?:\|(\w+))?\}")]
    private static partial Regex Placeholder();

    readonly Dictionary<string, Line> lines;

    WordBook(string language, Dictionary<string, Line> lines)
    {
        Language = language;
        this.lines = lines;
    }

    public string Language { get; }
    public IReadOnlyCollection<string> Keys => lines.Keys;
    public bool Has(string key) => lines.ContainsKey(key);

    /// <summary>The languages the app ships, from languages.json.</summary>
    public static IReadOnlyList<string> Shipped(string stringsRoot)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(stringsRoot, "languages.json")));
        return doc.RootElement.GetProperty("shipped").EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    /// <summary>
    /// Each regional variant's base, from languages.json's "variants" ("pt-BR" reads "pt"). A variant holds only the
    /// lines that read differently there: every other line is its base's.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Variants(string stringsRoot)
    {
        // A folder of words with no languages.json (a test's own) has no variants.
        var file = Path.Combine(stringsRoot, "languages.json");
        if (!File.Exists(file)) return new Dictionary<string, string>();
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        return doc.RootElement.TryGetProperty("variants", out var v) && v.ValueKind == JsonValueKind.Object
            ? v.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!)
            : new Dictionary<string, string>();
    }

    public static WordBook Load(string stringsRoot, string language)
    {
        // A regional variant is its base's lines with its own on top, as the shared repository's apps read it.
        if (Variants(stringsRoot).TryGetValue(language, out var baseLanguage))
        {
            var lines = Read(stringsRoot, baseLanguage);
            foreach (var (key, line) in Read(stringsRoot, language))
            {
                if (!lines.ContainsKey(key))
                    throw new InvalidDataException($"{language}: key '{key}' isn't one {baseLanguage} has");
                lines[key] = line;
            }
            return new WordBook(language, lines);
        }
        return new WordBook(language, Read(stringsRoot, language));
    }

    static Dictionary<string, Line> Read(string stringsRoot, string language)
    {
        var folder = Path.Combine(stringsRoot, language);
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"no words for '{language}' in {stringsRoot}. Run the pin sync first.");

        var lines = new Dictionary<string, Line>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var line = p.Value.ValueKind == JsonValueKind.String
                    ? new Line(p.Value.GetString()!, null)
                    : new Line(null, p.Value.EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetString()!));
                if (!lines.TryAdd(p.Name, line))
                    throw new InvalidDataException($"{language}: key '{p.Name}' is in two files");
            }
        }
        return lines;
    }

    /// <summary>A line with no count: values fill {name}, {name|possessive}, {name|this} and {name|This}.</summary>
    public string Get(string key, params (string Name, string Value)[] values)
    {
        var line = Find(key);
        if (line.Text is null) throw new ArgumentException($"'{key}' is a count: use Count", nameof(key));
        return Fill(key, line.Text, values);
    }

    /// <summary>A line with a count: picks the form the language gives this number, and fills {count} too.</summary>
    public string Count(string key, long count, params (string Name, string Value)[] values)
    {
        var line = Find(key);
        if (line.Forms is null) throw new ArgumentException($"'{key}' has no count: use Get", nameof(key));
        var form = PluralRules.Form(Language, count);
        var text = line.Forms.TryGetValue(form, out var t) ? t : line.Forms["other"];
        return Fill(key, text, values.Append(("count", count.ToString(CultureInfo.InvariantCulture))).ToArray());
    }

    /// <summary>The names a line fills, across all its forms, without the possessive marker.</summary>
    public IReadOnlySet<string> Placeholders(string key)
    {
        var line = Find(key);
        var texts = line.Text is not null ? new[] { line.Text } : line.Forms!.Values.ToArray();
        return texts.SelectMany(t => Placeholder().Matches(t).Select(m => m.Groups[1].Value)).ToHashSet();
    }

    /// <summary>The plural forms a count line has; empty for a line with no count.</summary>
    public IReadOnlySet<string> Forms(string key) =>
        (Find(key).Forms?.Keys ?? Enumerable.Empty<string>()).ToHashSet();

    /// <summary>True when a line uses {name|possessive}: only languages with a rule may.</summary>
    public bool UsesPossessive(string key)
    {
        var line = Find(key);
        var texts = line.Text is not null ? new[] { line.Text } : line.Forms!.Values.ToArray();
        return texts.Any(t => Placeholder().Matches(t).Any(m => m.Groups[2].Value == "possessive"));
    }

    /// <summary>True when a line uses {name|this} or {name|This}: only languages with a rule may.</summary>
    public bool UsesThis(string key)
    {
        var line = Find(key);
        var texts = line.Text is not null ? new[] { line.Text } : line.Forms!.Values.ToArray();
        return texts.Any(t => Placeholder().Matches(t).Any(m => m.Groups[2].Value is "this" or "This"));
    }

    /// <summary>"This name" in this book's language: "ce PC", "cet appareil"; <paramref name="capital"/> opens a sentence.</summary>
    public string ThisOf(string name, bool capital = false) => Demonstrative(Language, name, capital);

    /// <summary>"This name" by a language's rule (<see cref="This"/>); the name as it is in a language with none.</summary>
    public static string Demonstrative(string language, string name, bool capital = false)
    {
        if (name.Length == 0 || !This.TryGetValue(language, out var rule)) return name;
        var word = rule.Starts.Contains(char.ToLowerInvariant(name[0])) ? rule.Before : rule.Other;
        return (capital ? char.ToUpperInvariant(word[0]) + word[1..] : word) + name;
    }

    public string PossessiveOf(string name)
    {
        if (name.Length == 0 || !Possessive.TryGetValue(Language, out var rule)) return name;
        return name + (rule.Ends.Contains(char.ToLowerInvariant(name[^1])) ? rule.After : rule.Other);
    }

    Line Find(string key) =>
        lines.TryGetValue(key, out var line) ? line : throw new KeyNotFoundException($"{Language}: no words for '{key}'");

    string Fill(string key, string text, (string Name, string Value)[] values)
    {
        var byName = values.ToDictionary(v => v.Name, v => v.Value);
        // In a right-to-left language each value is set apart (U+2068 … U+2069), as the iPad's Words.isolated does, so a
        // name, a number or "#5098" keeps its own direction inside the line. A marker the caller splits on stays as it is.
        var isolate = Locale.RightToLeft(Language);
        return Placeholder().Replace(text, m =>
        {
            if (!byName.TryGetValue(m.Groups[1].Value, out var value))
                throw new ArgumentException($"'{key}' needs a value for {{{m.Groups[1].Value}}}");
            if (isolate && value.Length > 0 && !(value.Length == 1 && char.IsControl(value[0]))) value = Locale.Isolated(value);
            return m.Groups[2].Value switch
            {
                "" => value,
                "possessive" => PossessiveOf(value),
                "this" => ThisOf(value),
                "This" => ThisOf(value, capital: true),
                // A form this app doesn't read is an error, like a missing value: never a hole on screen.
                var form => throw new ArgumentException($"'{key}' asks for {{{m.Groups[1].Value}|{form}}}, a form this app doesn't read"),
            };
        });
    }

    sealed record Line(string? Text, Dictionary<string, string>? Forms);
}

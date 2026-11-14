using System.Text.Json;

namespace Tafs.Core.Contracts;

/// <summary>One case of a contract file with several: a request, and what tafs cloud answered.</summary>
public sealed record ContractCase(string Name, string Method, string Path, int Status, JsonElement Body);

/// <summary>Reads the contract fixtures in the shared repository's `contracts/`, at the pinned commit.</summary>
public static class ContractFile
{
    /// <summary>A file of `{about, cases: [{name, method, path, status, body}]}`, such as ai-consent.json.</summary>
    public static IReadOnlyList<ContractCase> Cases(string file)
    {
        var root = Parse(file);
        return root.GetProperty("cases").EnumerateArray()
            .Select(c => new ContractCase(
                c.GetProperty("name").GetString()!,
                c.GetProperty("method").GetString()!,
                c.GetProperty("path").GetString()!,
                c.GetProperty("status").GetInt32(),
                c.GetProperty("body").Clone()))
            .ToList();
    }

    /// <summary>A file of `{about, answer}`: the answer itself.</summary>
    public static JsonElement Answer(string file) => Parse(file).GetProperty("answer").Clone();

    static JsonElement Parse(string file)
    {
        if (!File.Exists(file))
            throw new FileNotFoundException($"{file} is missing. Run the pin sync first.");
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        return doc.RootElement.Clone();
    }
}

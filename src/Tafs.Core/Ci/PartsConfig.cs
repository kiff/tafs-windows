using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tafs.Core.Ci;

/// <summary>ci/parts.json: which tests belong to which part, and which paths a part owns.</summary>
public sealed class PartsConfig
{
    [JsonPropertyName("always")] public List<string> Always { get; set; } = new();
    [JsonPropertyName("all_on")] public List<string> AllOn { get; set; } = new();
    [JsonPropertyName("code_roots")] public List<string> CodeRoots { get; set; } = new();
    [JsonPropertyName("parts")] public Dictionary<string, List<string>> Parts { get; set; } = new();

    public static PartsConfig Parse(string json)
    {
        var config = JsonSerializer.Deserialize<PartsConfig>(json) ?? throw new InvalidDataException("parts: empty");
        if (config.Parts.Count == 0) throw new InvalidDataException("parts: no parts declared");
        foreach (var name in config.Always.Where(a => !config.Parts.ContainsKey(a)))
            throw new InvalidDataException($"parts: 'always' names unknown part '{name}'");
        foreach (var (name, globs) in config.Parts.Where(p => p.Value.Count == 0))
            throw new InvalidDataException($"parts: '{name}' owns no paths");
        return config;
    }
}

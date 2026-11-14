using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tafs.Core.Contracts;

/// <summary>
/// What tafs cloud answers the app, read with this app's own types. A field a type requires and an
/// answer lacks fails to decode: that is the contract test's whole job. Fields the app doesn't use
/// are ignored.
/// </summary>
public static class Answers
{
    static readonly JsonSerializerOptions Options = new()
    {
        // .NET 8 already refuses an answer that lacks a `required` member.
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static T Decode<T>(JsonElement json) =>
        json.Deserialize<T>(Options) ?? throw new JsonException($"{typeof(T).Name}: the answer is null");
}

// AI-data consent: GET/POST/DELETE /v1/me/ai-consent, and the refusal of a turn without it.
public sealed record ConsentWords
{
    public required string Title { get; init; }
    public required List<string> Lines { get; init; }
    public required string Allow { get; init; }
    public required string NotNow { get; init; }
    public required string Withdraw { get; init; }
    public required string Given { get; init; }
}

public sealed record Consent
{
    public required bool Given { get; init; }
    public required string Version { get; init; }
    public required ConsentWords Words { get; init; }
}

public sealed record ConsentAnswer { public required Consent Consent { get; init; } }

/// <summary>
/// A 403 `ai_consent_required` (it has a code) or a 409 from an older screen (it has none):
/// the error and the current consent.
/// </summary>
public sealed record ConsentRefusal
{
    public required string Error { get; init; }
    public string? Code { get; init; }
    public required Consent Consent { get; init; }
}

// Coach's first start: GET /v1/setup.
public sealed record SetupAnswer
{
    public string? Setup { get; init; }
    public string? Tools { get; init; }
    public string? TimeZone { get; init; }
    public SetupStart? Start { get; init; }
}

public sealed record SetupStart
{
    public required string State { get; init; }
    public required string Scheme { get; init; }
    public required string AgentId { get; init; }
    public required List<SetupRequest> Requests { get; init; }
    public required CoachToolsStep CoachToolsStep { get; init; }
    public required StartWords Words { get; init; }
}

public sealed record SetupRequest
{
    public required string Id { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
}

public sealed record CoachToolsStep
{
    public required string Title { get; init; }
    public required string Give { get; init; }
    public required string NotNow { get; init; }
}

public sealed record StartWords
{
    public required string Lead { get; init; }
    public required string Start { get; init; }
}

// Home's part of GET /v1/recipes.
public sealed record RecipesHome
{
    public required bool Shown { get; init; }
    public required Suggestions Suggestions { get; init; }
    public required HomeWords Home { get; init; }
}

public sealed record Suggestions
{
    public required bool Show { get; init; }
    public required string Reason { get; init; }
    public required string Words { get; init; }
}

public sealed record HomeWords
{
    public required bool Shown { get; init; }
    public required string Words { get; init; }
    public required OwnWords OwnWords { get; init; }
}

public sealed record OwnWords
{
    public required string Title { get; init; }
    public required string Line { get; init; }
}

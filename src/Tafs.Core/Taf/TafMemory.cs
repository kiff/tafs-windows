using System.Text.Json;

namespace Tafs.Core.Taf;

/// <summary>
/// What a taf remembers, every note (kiff/tafs docs/api.md, <c>GET /v1/tafs/{id}/memory</c>), as the iPad's MemoryPanel opened
/// from See all: the shared notes and the person's own, each of which Forget removes for good
/// (<c>DELETE /v1/tafs/{id}/memory/{note}</c>).
/// </summary>
public static class TafMemory
{
    /// <summary>Every note (<c>?all=true</c>), as the iPad's <c>loadAllMemory</c>.</summary>
    public static string Path(string taf) => $"/v1/tafs/{CloudRead.PathPart(taf)}/memory?all=true";

    public static string NotePath(string taf, string note) => $"/v1/tafs/{CloudRead.PathPart(taf)}/memory/{CloudRead.PathPart(note)}";

    /// <summary>The notes, in tafs cloud's order; one without its id, path, words or time is left out. None when it doesn't read.</summary>
    public static IReadOnlyList<MemoryNote>? Read(JsonElement answer) =>
        Lenient.Field(answer, "notes") is { ValueKind: JsonValueKind.Array } notes
            ? notes.EnumerateArray().Select(n => Lenient.Text(n, "id") is { } id && Lenient.Text(n, "path") is { } path
                    && Lenient.Text(n, "content") is { } content && Lenient.At(n, "updated_at") is { } at
                    ? new MemoryNote(id, path, content, at) : null).OfType<MemoryNote>().ToList()
            : null;

    /// <summary>Forgotten: done, or already gone (404); else why not, as the iPad's "It wasn't forgotten. …".</summary>
    public static string? Forgotten(Words.WordBook w, CloudRead answer) =>
        answer.Answer is not null || answer.Status == 404 ? null : w.Get("taf.memory.not_forgotten", ("why", answer.Why ?? w.Get("error.offline")));
}

using System.Text;
using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>One field the application sends: its name, its type, and for a choice its values.</summary>
public sealed record DoorDraftField(string Name, string Type, IReadOnlyList<string>? Values);

/// <summary>
/// The door Coach drafted for a taf (<c>GET /v1/tafs/{id}/doors/drafts/{draft}</c>, <c>contracts/doors-api-draft.json</c>), as the
/// iPad's <c>TafsCloud.DoorDraft</c>: what's drawn is read from <c>body</c>, the bytes this PC signs and sends, never from a
/// copy beside it, so the owner approves exactly what they see. <paramref name="Body"/> is <c>body</c> in its canonical form
/// (RFC 8785): what's signed, and the same bytes posted to <c>…/doors</c>.
/// </summary>
public sealed record DoorDraft(string Id, string Name, IReadOnlyList<DoorDraftField> Fields, string Job, IReadOnlyList<string> Outcomes, bool Text,
    byte[] Body);

/// <summary>The door an approval opened, and its first key, shown in this one answer only.</summary>
public sealed record AddedApiDoor(Door Door, IssuedKey Key);

public static class ApiDoorDrafts
{
    public static string DraftPath(string taf, string draft) => TafDoors.Path(taf) + "/drafts/" + CloudRead.PathPart(draft);

    /// <summary>tafs cloud's draft; none for an answer with no draft, a body that isn't an <c>api</c> door's, or a draft with no name or job.</summary>
    public static DoorDraft? Read(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object || Lenient.Text(answer, "draft_id") is not { Length: > 0 } id
            || Lenient.Field(answer, "body") is not { ValueKind: JsonValueKind.Object } body || Lenient.Text(body, "kind") != "api"
            || Lenient.Field(body, "draft") is not { ValueKind: JsonValueKind.Object } d) return null;
        var name = Lenient.Text(d, "name")?.Trim() ?? "";
        var job = Lenient.Text(d, "job")?.Trim() ?? "";
        if (name.Length == 0 || job.Length == 0) return null;
        var fields = Lenient.Array(d, "fields")
            .Where(f => f.ValueKind == JsonValueKind.Object && Lenient.Text(f, "name") is not null && Lenient.Text(f, "type") is not null)
            .Select(f => new DoorDraftField(Lenient.Text(f, "name")!, Lenient.Text(f, "type")!,
                Lenient.Field(f, "values") is { ValueKind: JsonValueKind.Array } vs ? vs.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList() : null))
            .ToList();
        var outcomes = Lenient.Array(d, "outcomes").Where(o => o.ValueKind == JsonValueKind.String).Select(o => o.GetString()!).ToList();
        return new(id, name, fields, job, outcomes, Lenient.Bool(d, "text"), Jcs.Canonical(Encoding.UTF8.GetBytes(body.GetRawText())));
    }

    /// <summary>A field as a developer reads it: its name, then its choices or its type.</summary>
    public static string Field(DoorDraftField f) => f.Name + " · " + (f.Values is { Count: > 0 } v ? string.Join(" | ", v) : f.Type);

    /// <summary>The approval's answer: the door it opened and its first key; none when it isn't both.</summary>
    public static AddedApiDoor? ReadAdded(JsonElement answer) =>
        answer.ValueKind == JsonValueKind.Object && TafDoors.Changed(answer) is { } door && Lenient.Field(answer, "key") is { } k && ApiDoor.ReadIssued(k) is { } key
            ? new(door, key) : null;

    /// <summary>Whether tafs cloud refused the read because a door was already made from this draft (410 <c>draft_used</c>).</summary>
    public static bool Used(CloudRead read) => read.Code == "draft_used";

    /// <summary>"refunded, refused and needs_review", in the reader's language's list.</summary>
    public static string OutcomesLine(WordBook w, DoorDraft d) => w.Get("door.api.draft_back", ("outcomes", Lists.And(w.Language, d.Outcomes)));
}

using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Account;

/// <summary>Someone in a group: their KIFF subject, and manager or member (the iPad's <c>GroupMember</c>).</summary>
public sealed record GroupMember(string Subject, string Role)
{
    public bool IsManager => Role == "manager";
}

/// <summary>
/// One of the account's groups, as KIFF lists it (the iPad's <c>KIFFClient.Group</c>): its people, and the agents it's
/// shared. <see cref="Everyone"/>: KIFF's default group, which has everyone on the account in it.
/// </summary>
public sealed record PeopleGroup(string Id, string Name, bool? Everyone, IReadOnlyList<GroupMember> Members, IReadOnlyList<string> Agents);

/// <summary>One of the account's people, as KIFF lists them (the iPad's <c>Person</c>). KIFF names them by email.</summary>
public sealed record Person(string Subject, string Role, string Status, string? Email = null, bool? You = null)
{
    public bool Active => Status == "active";

    /// <summary>What the app shows: their email, else KIFF's id for them.</summary>
    public string Label => string.IsNullOrEmpty(Email) ? Subject : Email;
}

/// <summary>
/// People &amp; groups, as the iPad's <c>PeopleGroupsView</c> and <c>refreshPeople</c>: the account's groups, each with its
/// people and the tafs it uses. It only reads: making, renaming or deleting a group, and changing its people, are signed
/// acts on the iPad (<c>changePeople</c>).
/// </summary>
public static class People
{
    public const string GroupsPath = "/v1/groups";
    public const string PeoplePath = "/v1/tenant/members";

    /// <summary>
    /// <c>{"groups": […]}</c>, as the iPad's synthesized decoder: each group's id, name, members (each its subject and role)
    /// and agents are required, <c>everyone</c> absent or null when not said. Anything else fails the read. Sorted as
    /// <c>refreshPeople</c> keeps them: KIFF's default group first, then by name, lowercased.
    /// </summary>
    public static IReadOnlyList<PeopleGroup>? Groups(JsonElement answer)
    {
        if (List(answer, "groups") is not { } list) return null;
        var groups = new List<PeopleGroup>();
        foreach (var g in list)
        {
            if (Text(g, "id") is not { } id || Text(g, "name") is not { } name || !Flag(g, "everyone", out var everyone)
                || List(g, "members") is not { } members || List(g, "agents") is not { } agents)
                return null;
            var people = new List<GroupMember>();
            foreach (var m in members)
            {
                if (Text(m, "subject") is not { } subject || Text(m, "role") is not { } role) return null;
                people.Add(new(subject, role));
            }
            if (agents.Any(a => a.ValueKind != JsonValueKind.String)) return null;
            groups.Add(new(id, name, everyone, people, agents.Select(a => a.GetString()!).ToList()));
        }
        return groups.OrderBy(g => g.Everyone == true ? 0 : 1).ThenBy(g => g.Name.ToLowerInvariant(), StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// <c>{"members": […]}</c>, as the iPad's <c>Person</c>: subject, role and status required, email and you optional.
    /// Anything else fails the read. Only the people on the account now, by label, lowercased.
    /// </summary>
    public static IReadOnlyList<Person>? Members(JsonElement answer)
    {
        if (List(answer, "members") is not { } list) return null;
        var people = new List<Person>();
        foreach (var p in list)
        {
            if (Text(p, "subject") is not { } subject || Text(p, "role") is not { } role || Text(p, "status") is not { } status
                || !OptionalText(p, "email", out var email) || !Flag(p, "you", out var you))
                return null;
            people.Add(new(subject, role, status, email, you));
        }
        return people.Where(p => p.Active).OrderBy(p => p.Label.ToLowerInvariant(), StringComparer.Ordinal).ToList();
    }

    /// <summary>The account as the person knows it, from <c>GET /v1/me</c>: its name (else its slug) and their email.</summary>
    public static (string Name, string? Email)? Account(JsonElement? me)
    {
        if (me is not { ValueKind: JsonValueKind.Object } a || Text(a, "tenant_slug") is not { } slug) return null;
        var display = a.TryGetProperty("tenant_display_name", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
        var email = a.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        return (string.IsNullOrEmpty(display) ? slug : display, email);
    }

    /// <summary>
    /// The first line (the iPad's <c>AccountLines.people</c>): "Acme · 3 people", or the count alone when the account is
    /// named after its one owner's email.
    /// </summary>
    public static string AccountLine(WordBook w, string account, string? email, int count)
    {
        var people = w.Count("people.count", count);
        var mail = email?.Trim();
        var namedAfterEmail = !string.IsNullOrEmpty(mail) && string.Equals(mail, account.Trim(), StringComparison.OrdinalIgnoreCase);
        return namedAfterEmail ? people : $"{account} · {people}";
    }

    /// <summary>The group's name as read: KIFF's default group in the app's language, any other as named.</summary>
    public static string ShownName(WordBook w, PeopleGroup g) => g.Everyone == true ? w.Get("people.everyone_group") : g.Name;

    /// <summary>"Uses Refunds and Invoices", or that it uses none yet (the iPad's <c>uses</c>): only the agents that are the account's tafs.</summary>
    public static string Uses(WordBook w, PeopleGroup g, Func<string, string?> tafName)
    {
        var names = g.Agents.Select(tafName).OfType<string>().Order(StringComparer.Ordinal).ToList();
        if (names.Count == 0) return w.Get(g.Everyone == true ? "people.everyone_uses_none" : "people.uses_none");
        return w.Get("people.uses", ("tafs", Lists.And(w.Language, names)));
    }

    /// <summary>Managers first, then by label, lowercased (the iPad's <c>managersFirst</c>).</summary>
    public static IReadOnlyList<GroupMember> ManagersFirst(PeopleGroup g, Func<string, Person?> person) =>
        g.Members.OrderBy(m => m.IsManager ? 0 : 1).ThenBy(m => (person(m.Subject)?.Label ?? m.Subject).ToLowerInvariant(), StringComparer.Ordinal).ToList();

    /// <summary>A person's initial, as the iPad's <c>PersonBadge</c>: their label's first letter, upper case.</summary>
    public static string Initial(string label) => label.Length == 0 ? "" : label[..1].ToUpperInvariant();

    static List<JsonElement>? List(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : null;

    static string? Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static bool OptionalText(JsonElement e, string name, out string? value)
    {
        value = null;
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.String) return false;
        value = v.GetString();
        return true;
    }

    static bool Flag(JsonElement e, string name, out bool? value)
    {
        value = null;
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = v.GetBoolean();
        return true;
    }
}

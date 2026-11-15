using System.Text.Json;

namespace Tafs.Core.Coach;

/// <summary>
/// Who may use Coach, as the iPad's <c>AppModel.mayUseCoach</c>: KIFF's answer for Setup's agent (<c>setup</c>) in
/// <c>GET /v1/me/access</c> says whether they may use it; without one, the account's <c>members.manage</c> capability from
/// <c>GET /v1/me</c>, so an account admin by default. Nothing read is no.
/// </summary>
public static class CoachAccess
{
    public const string AccessPath = "/v1/me/access";
    public const string MePath = "/v1/me";

    public static bool MayUseCoach(bool? setupUse, IReadOnlyList<string>? capabilities) =>
        setupUse ?? capabilities?.Contains("members.manage") ?? false;

    /// <summary>
    /// Setup's <c>use</c>, as the iPad's <c>myAccess</c> decodes <c>{"agents": {…}}</c> with <c>try?</c>: every agent's
    /// <c>use</c>, <c>oversee</c> and <c>manage</c> are true or false, its <c>groups</c> absent, null or words, or nothing
    /// is read. None without Setup's agent.
    /// </summary>
    public static bool? SetupUse(JsonElement? answer) =>
        Agents(answer) is { } agents && agents.TryGetProperty("setup", out var setup) ? setup.GetProperty("use").GetBoolean() : null;

    /// <summary>
    /// Which agents the person manages, by agent, as the iPad's <c>access[agent]?.manage</c>: its Card, versions, halting it
    /// and removing it. None when the access doesn't read; an agent not listed isn't managed (the iPad's <c>manages</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, bool>? Manage(JsonElement? answer) =>
        Agents(answer)?.EnumerateObject().ToDictionary(a => a.Name, a => a.Value.GetProperty("manage").GetBoolean());

    /// <summary><c>{"agents": {…}}</c>, every agent read as the iPad's synthesized <c>Access</c>, or none.</summary>
    static JsonElement? Agents(JsonElement? answer)
    {
        if (answer is not { } a || !Shape.Required(a, "agents", v => v.ValueKind == JsonValueKind.Object)) return null;
        var agents = a.GetProperty("agents");
        foreach (var agent in agents.EnumerateObject())
            if (!Shape.Required(agent.Value, "use", Shape.Bool) || !Shape.Required(agent.Value, "oversee", Shape.Bool)
                || !Shape.Required(agent.Value, "manage", Shape.Bool) || !Shape.Optional(agent.Value, "groups", g => Shape.List(g, Shape.Text)))
                return null;
        return agents;
    }

    /// <summary>
    /// What the person may do on the account, as the iPad's <c>Account</c> decodes <c>GET /v1/me</c>: its tenant's id and
    /// slug are words, its name, email and time zone absent, null or words, its capabilities absent, null or a list of words.
    /// None when it doesn't read, or doesn't say.
    /// </summary>
    public static IReadOnlyList<string>? Capabilities(JsonElement? answer)
    {
        if (answer is not { } a || !Shape.Required(a, "tenant_id", Shape.Text) || !Shape.Required(a, "tenant_slug", Shape.Text)
            || !((string[])["tenant_display_name", "email", "time_zone"]).All(f => Shape.Optional(a, f, Shape.Text))
            || !Shape.Optional(a, "capabilities", v => Shape.List(v, Shape.Text))) return null;
        return a.TryGetProperty("capabilities", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().Select(x => x.GetString()!).ToList() : null;
    }
}

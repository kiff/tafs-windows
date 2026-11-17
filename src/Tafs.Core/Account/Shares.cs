using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Account;

/// <summary>Who a taf's agent is shared with, as the iPad's <c>KIFFClient.Share</c>: a group, or one person.</summary>
public sealed record TafShare(string? Group, string? Subject);

/// <summary>
/// Sharing a taf (P32), as the iPad's <c>ShareTafSheet</c>: who uses it (a group, or one person), then what that means for
/// them, then Share. KIFF keeps it on the taf's agent: <c>GET /v1/agents/{id}/grants</c>; sharing with someone gives them
/// its use, so it's an approval (<c>POST …/grants</c>, this PC's passkey behind Windows Hello); taking it away is this
/// session's key (<c>DELETE …/grants?group=</c> or <c>?subject=</c>), signed.
/// </summary>
public static class Shares
{
    public static string Path(string agent) => "/v1/agents/" + CloudRead.PathPart(agent) + "/grants";

    /// <summary>A share names exactly one group or one person, never neither: an empty one could read as anyone at KIFF.</summary>
    public static bool Names(TafShare share) =>
        (share.Group is { Length: > 0 }) != (share.Subject is { Length: > 0 });

    /// <summary>Taking one away: by its group, else by its person. One that names neither is never built.</summary>
    /// <exception cref="ArgumentException">The share names no group and no person, or both.</exception>
    public static string RemovePath(string agent, TafShare share) => Names(share)
        ? Path(agent) + (share.Group is { } g ? "?group=" + Uri.EscapeDataString(g) : "?subject=" + Uri.EscapeDataString(share.Subject!))
        : throw new ArgumentException("a share names one group or one person", nameof(share));

    /// <summary>What sharing sends: <c>{"group":…}</c> or <c>{"subject":…}</c>, as the iPad's encoder leaves out the other.</summary>
    public static byte[] AddBody(TafShare share) => share.Group is { } g
        ? JsonSerializer.SerializeToUtf8Bytes(new { group = g })
        : JsonSerializer.SerializeToUtf8Bytes(new { subject = share.Subject });

    /// <summary><c>{"grants": […]}</c>: each a group or a person; anything else fails the read.</summary>
    public static IReadOnlyList<TafShare>? Read(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty("grants", out var list) || list.ValueKind != JsonValueKind.Array) return null;
        var shares = new List<TafShare>();
        foreach (var s in list.EnumerateArray())
        {
            var group = Lenient.Text(s, "group");
            var subject = Lenient.Text(s, "subject");
            if (s.ValueKind != JsonValueKind.Object || (group is null && subject is null)) return null;
            shares.Add(new(group, group is null ? subject : null));
        }
        return shares;
    }

    /// <summary>"Maria (manager), Ana, Luis", or "No one yet", as the iPad's <c>people(of:)</c>.</summary>
    public static string PeopleOf(WordBook w, PeopleGroup group, Func<string, Person?> person)
    {
        var names = People.ManagersFirst(group, person).Select(m =>
        {
            var label = person(m.Subject)?.Label ?? m.Subject;
            return m.IsManager ? w.Get("people.name_manager", ("name", label)) : label;
        }).ToList();
        return names.Count == 0 ? w.Get("people.no_one_yet") : string.Join(", ", names);
    }

    /// <summary>
    /// What the change means, as it holds today (the iPad's <c>consequences</c>): each person's own chats when it's shared at
    /// all; who uses it, a group with its people; and who stops using it.
    /// </summary>
    public static IReadOnlyList<string> Consequences(WordBook w, string taf, IReadOnlyCollection<TafShare> had, IReadOnlyCollection<TafShare> chosen,
        IReadOnlyList<PeopleGroup> groups, Func<string, Person?> person)
    {
        var lines = new List<string>();
        if (chosen.Count > 0) lines.Add(w.Get("people.own_chats"));
        foreach (var share in chosen.OrderBy(s => s.Group ?? s.Subject ?? "", StringComparer.Ordinal))
        {
            var group = share.Group is { } id ? groups.FirstOrDefault(g => g.Id == id) : null;
            if (group?.Everyone == true) lines.Add(w.Get("people.everyone_uses", ("taf", taf), ("people", PeopleOf(w, group, person))));
            else if (group is not null) lines.Add(w.Get("people.group_uses", ("group", People.ShownName(w, group)), ("taf", taf), ("people", PeopleOf(w, group, person))));
            else if (share.Subject is { } subject) lines.Add(w.Get("people.person_uses", ("person", person(subject)?.Label ?? subject), ("taf", taf)));
        }
        foreach (var share in had.Except(chosen))
        {
            var name = share.Group is { } id ? groups.FirstOrDefault(g => g.Id == id) is { } g ? People.ShownName(w, g) : "" : share.Subject is { } s ? person(s)?.Label ?? s : "";
            lines.Add(w.Get("people.stops_using", ("who", name), ("taf", taf)));
        }
        return lines;
    }

    /// <summary>
    /// Sharing as chosen, in the iPad's order: each new share approved by this PC's passkey (Windows Hello each), then each one
    /// taken away signed by this session's key; with nothing new to share, one Windows Hello confirms the change first. None when
    /// it all went; else why: "Share Refunds didn't go through." with KIFF's reason. Windows Hello closed says nothing. A sign-out
    /// meanwhile ends it quietly.
    /// </summary>
    public static async Task<(bool Done, string? Why)> ApplyAsync(WordBook w, string tenant, string taf, string agent,
        IReadOnlyCollection<TafShare> add, IReadOnlyCollection<TafShare> remove,
        Func<string, byte[], Task<(string Header, string Value)>> approve,
        Func<string, Task<StepConfirmed>> confirm, Func<byte[], Task<byte[]>> signDer, Func<string> signedAt,
        Func<string, string, byte[], (string Header, string Value), Task<(int Status, string Body)>> send, Func<bool> current)
    {
        var what = w.Get("people.share.title", ("taf", taf));
        string Failed(string why) => w.Get("common.didnt_go_through", ("what", what), ("why", why));
        // A share that names no one, or two, is never signed: nothing goes, said as the app's own error.
        if (!add.Concat(remove).All(Names)) return (false, Failed(w.Get("error.ours")));
        if (add.Count == 0)
        {
            var confirmed = await confirm(what);
            if (!current()) return (false, null);
            if (confirmed == StepConfirmed.Failed) return (false, Failed(w.Get("common.not_sent_unconfirmed")));
            if (confirmed != StepConfirmed.Yes) return (false, null);
        }
        try
        {
            foreach (var share in add)
            {
                var path = Path(agent);
                var body = AddBody(share);
                var signature = await approve(path, body);
                if (!current()) return (false, null);
                var (status, _) = await send("POST", path, body, signature);
                if (!current()) return (false, null);
                if (status is < 200 or > 299) return (false, Failed(KiffErrors.Words(w, status)));
            }
            foreach (var share in remove)
            {
                var path = RemovePath(agent, share);
                var at = signedAt();
                var header = KiffSigning.SignatureHeaderV1(at, await signDer(KiffSigning.RequestV1(tenant, "DELETE", path, [], at)));
                if (!current()) return (false, null);
                var (status, _) = await send("DELETE", path, [], ("KIFF-Device-Signature", header));
                if (!current()) return (false, null);
                if (status is < 200 or > 299) return (false, Failed(KiffErrors.Words(w, status)));
            }
        }
        catch (OperationCanceledException) { return (false, null); }
        catch (Exception e) when (e is PasskeyRefusedException or CryptographicException or COMException or InvalidOperationException
            or UnauthorizedAccessException or InvalidDataException)
        {
            return (false, current() ? Failed(w.Get("common.not_sent_unconfirmed")) : null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (false, current() ? Failed(w.Get("error.offline")) : null);
        }
        return (true, null);
    }
}

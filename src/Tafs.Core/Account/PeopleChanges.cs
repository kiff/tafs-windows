using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Account;

/// <summary>One change to groups, as KIFF takes it: its method, path and body, and what it is in words for Windows Hello.</summary>
public sealed record PeopleChange(string Method, string Path, byte[] Body, string What);

/// <summary>
/// Changing People &amp; groups from this PC (P24), as the iPad's <c>changePeople</c>: making a group, renaming or deleting one,
/// adding someone, making them a manager or a member, removing them. Each is the person's act behind Windows Hello, then this
/// session's TPM key signs exactly that request (<c>KIFF-Device-Signature</c>, <c>kiff.request.v1</c>), as KIFF asks of a phone
/// for these routes; KIFF's handlers decide who may make it. Then everything is read again.
/// </summary>
public static class PeopleChanges
{
    public static string GroupPath(string group) => People.GroupsPath + "/" + CloudRead.PathPart(group);

    public static string MemberPath(string group, string subject) => GroupPath(group) + "/members/" + CloudRead.PathPart(subject);

    static byte[] Json(object body) => JsonSerializer.SerializeToUtf8Bytes(body);

    public static PeopleChange Create(WordBook w, string name) =>
        new("POST", People.GroupsPath, Json(new { name }), w.Get("people.create_group", ("name", name)));

    public static PeopleChange Rename(WordBook w, PeopleGroup group, string name) =>
        new("PATCH", GroupPath(group.Id), Json(new { name }), w.Get("people.rename_group", ("group", People.ShownName(w, group)), ("name", name)));

    public static PeopleChange Delete(WordBook w, PeopleGroup group) =>
        new("DELETE", GroupPath(group.Id), [], w.Get("people.delete_group", ("group", People.ShownName(w, group))));

    /// <summary>Adding someone, or changing their role: manager or member.</summary>
    public static PeopleChange SetRole(WordBook w, PeopleGroup group, string subject, string who, string role, bool adding) =>
        new("PUT", MemberPath(group.Id, subject), Json(new { role }),
            adding ? w.Get("people.add_to_group", ("who", who), ("group", People.ShownName(w, group)))
                : w.Get(role == "manager" ? "people.make_role_manager" : "people.make_role_member", ("who", who), ("group", People.ShownName(w, group))));

    public static PeopleChange Remove(WordBook w, PeopleGroup group, string subject, string who) =>
        new("DELETE", MemberPath(group.Id, subject), [], w.Get("people.remove_from_group", ("who", who), ("group", People.ShownName(w, group))));

    /// <summary>A name to send: trimmed; none when nothing's left.</summary>
    public static string? Name(string typed) => typed.Trim() is { Length: > 0 } name ? name : null;

    /// <summary>Whoever manages people (KIFF's <c>members.manage</c>): groups, their names, and who manages each.</summary>
    public static bool MayManagePeople(IReadOnlyList<string>? capabilities) => capabilities?.Contains("members.manage") ?? false;

    /// <summary>Whether this person manages the group, as the iPad's <c>managesGroup</c>: they manage people, or they're its manager.</summary>
    public static bool Manages(PeopleGroup group, bool managesPeople, Func<string, Person?> person) =>
        managesPeople || group.Members.Any(m => m.IsManager && person(m.Subject)?.You == true);

    /// <summary>
    /// Who may remove someone, as the iPad's <c>mayRemove</c>: a group's manager removes its members; removing a manager is
    /// for whoever manages people. No one is removed from KIFF's default group, which is everyone on the account.
    /// </summary>
    public static bool MayRemove(GroupMember member, PeopleGroup group, bool managesPeople, bool managesGroup) =>
        group.Everyone != true && (managesPeople || (managesGroup && !member.IsManager));

    /// <summary>Who's in the account and not in the group yet: whom it can add.</summary>
    public static IReadOnlyList<Person> Outside(PeopleGroup group, IReadOnlyList<Person> people) =>
        people.Where(p => group.Members.All(m => m.Subject != p.Subject)).ToList();

    /// <summary>
    /// One change: Windows Hello naming it, then signed and sent. None when it went; Windows Hello closed says nothing, as the
    /// iPad's; a refusal says "Rename Support to Help didn't go through." with KIFF's reason in plain words. A sign-out meanwhile
    /// ends it quietly.
    /// </summary>
    public static async Task<(bool Done, string? Why)> MakeAsync(WordBook w, string tenant, PeopleChange change,
        Func<string, Task<StepConfirmed>> confirm, Func<byte[], Task<byte[]>> signDer,
        Func<PeopleChange, string, Task<(int Status, string Body)>> send, Func<string> signedAt, Func<bool> current)
    {
        string Failed(string why) => w.Get("common.didnt_go_through", ("what", change.What), ("why", why));
        var confirmed = await confirm(change.What);
        if (!current()) return (false, null);
        if (confirmed == StepConfirmed.Failed) return (false, Failed(w.Get("common.not_sent_unconfirmed")));
        if (confirmed != StepConfirmed.Yes) return (false, null);

        var at = signedAt();
        string header;
        try { header = KiffSigning.SignatureHeaderV1(at, await signDer(KiffSigning.RequestV1(tenant, change.Method, change.Path, change.Body, at))); }
        catch (Exception e) when (e is CryptographicException or COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (false, current() ? Failed(w.Get("common.not_sent_unconfirmed")) : null);
        }
        if (!current()) return (false, null);

        int status;
        try { (status, _) = await send(change, header); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { status = 0; }
        if (!current()) return (false, null);
        return status is >= 200 and <= 299 ? (true, null) : (false, Failed(status == 0 ? w.Get("error.offline") : KiffErrors.Words(w, status)));
    }
}

using System.Text;
using Tafs.Core.Account;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Account;

/// <summary>People &amp; groups' changes from this PC (P24), as the iPad's changePeople and its sheets.</summary>
[Part("account")]
public class PeopleChangesTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static readonly PeopleGroup Support = new("grp_1", "Support", null, [new("sub_ana", "manager"), new("sub_bo", "member")], []);
    static readonly PeopleGroup Everyone = new("grp_0", "Everyone", true, [new("sub_ana", "member")], []);
    static readonly Person Ana = new("sub_ana", "member", "active", "ana@x.test", You: true);
    static readonly Person Bo = new("sub_bo", "member", "active", "bo@x.test");
    static readonly Person Cy = new("sub_cy", "member", "active", "cy@x.test");

    [Fact]
    public void Each_change_is_its_route_body_and_words()
    {
        var create = PeopleChanges.Create(En, "Finance");
        Assert.Equal(("POST", "/v1/groups", """{"name":"Finance"}""", "Create the group Finance"),
            (create.Method, create.Path, Encoding.UTF8.GetString(create.Body), create.What));
        var rename = PeopleChanges.Rename(En, Support, "Help");
        Assert.Equal(("PATCH", "/v1/groups/grp_1", """{"name":"Help"}""", "Rename Support to Help"),
            (rename.Method, rename.Path, Encoding.UTF8.GetString(rename.Body), rename.What));
        var delete = PeopleChanges.Delete(En, Support);
        Assert.Equal(("DELETE", "/v1/groups/grp_1", 0, "Delete the group Support"), (delete.Method, delete.Path, delete.Body.Length, delete.What));
        var add = PeopleChanges.SetRole(En, Support, "sub_cy", "cy@x.test", "member", adding: true);
        Assert.Equal(("PUT", "/v1/groups/grp_1/members/sub_cy", """{"role":"member"}""", "Add cy@x.test to Support"),
            (add.Method, add.Path, Encoding.UTF8.GetString(add.Body), add.What));
        Assert.Equal("Make bo@x.test a manager of Support", PeopleChanges.SetRole(En, Support, "sub_bo", "bo@x.test", "manager", adding: false).What);
        var remove = PeopleChanges.Remove(En, Support, "sub_bo", "bo@x.test");
        Assert.Equal(("DELETE", "/v1/groups/grp_1/members/sub_bo", "Remove bo@x.test from Support"), (remove.Method, remove.Path, remove.What));
        Assert.Null(PeopleChanges.Name("   "));
        Assert.Equal("Help", PeopleChanges.Name("  Help "));
    }

    [Fact]
    public void Who_may_do_what_as_KIFF_allows()
    {
        Person? Find(string s) => new[] { Ana, Bo, Cy }.FirstOrDefault(p => p.Subject == s);
        Assert.True(PeopleChanges.MayManagePeople(["members.manage"]));
        Assert.False(PeopleChanges.MayManagePeople(null));
        // Ana manages Support without managing people; Bo doesn't.
        Assert.True(PeopleChanges.Manages(Support, managesPeople: false, Find));
        Assert.False(PeopleChanges.Manages(Support, managesPeople: false, s => s == "sub_ana" ? Ana with { You = false } : Find(s)));
        // A group's manager removes its members, not its managers; whoever manages people removes either; no one leaves everyone.
        Assert.True(PeopleChanges.MayRemove(Support.Members[1], Support, managesPeople: false, managesGroup: true));
        Assert.False(PeopleChanges.MayRemove(Support.Members[0], Support, managesPeople: false, managesGroup: true));
        Assert.True(PeopleChanges.MayRemove(Support.Members[0], Support, managesPeople: true, managesGroup: true));
        Assert.False(PeopleChanges.MayRemove(Everyone.Members[0], Everyone, managesPeople: true, managesGroup: true));
        Assert.Equal([Cy], PeopleChanges.Outside(Support, [Ana, Bo, Cy]));
    }

    [Fact]
    public async Task Confirmed_then_signed_over_exactly_that_request_then_sent()
    {
        var said = new List<string>();
        byte[]? signed = null;
        var change = PeopleChanges.Rename(En, Support, "Help");
        var (done, why) = await PeopleChanges.MakeAsync(En, "t_1", change,
            prompt => { said.Add("hello " + prompt); return Task.FromResult(StepConfirmed.Yes); },
            message => { signed = message; return Task.FromResult(new byte[] { 1, 2 }); },
            (c, h) => { said.Add($"send {c.Method} {c.Path} {h.Header} {h.Value[..h.Value.IndexOf(',')]}"); return Task.FromResult((204, "")); },
            () => "2026-11-17T06:00:00.000Z", () => true);
        Assert.Equal((true, (string?)null), (done, why));
        Assert.Equal(["hello Rename Support to Help", "send PATCH /v1/groups/grp_1 KIFF-Device-Signature t=2026-11-17T06:00:00.000Z"], said);
        Assert.Equal(KiffSigning.RequestV1("t_1", "PATCH", "/v1/groups/grp_1", change.Body, "2026-11-17T06:00:00.000Z"), signed);
    }

    [Fact]
    public async Task Windows_Hello_closed_sends_nothing_and_a_refusal_says_why()
    {
        var sent = false;
        var change = PeopleChanges.Delete(En, Support);
        Task<(int, string)> Send(PeopleChange c, (string, string) h) { sent = true; return Task.FromResult((204, "")); }
        Assert.Equal((false, (string?)null), await PeopleChanges.MakeAsync(En, "t", change, _ => Task.FromResult(StepConfirmed.Cancelled),
            _ => Task.FromResult(new byte[1]), Send, () => "x", () => true));
        Assert.False(sent);
        Assert.Equal("Delete the group Support didn't go through. Not sent: it wasn't confirmed.",
            (await PeopleChanges.MakeAsync(En, "t", change, _ => Task.FromResult(StepConfirmed.Failed), _ => Task.FromResult(new byte[1]), Send, () => "x", () => true)).Why);
        Assert.False(sent);

        var refused = await PeopleChanges.MakeAsync(En, "t", change, _ => Task.FromResult(StepConfirmed.Yes), _ => Task.FromResult(new byte[1]),
            (_, _) => Task.FromResult((403, "{}")), () => "x", () => true);
        Assert.False(refused.Done);
        Assert.StartsWith("Delete the group Support didn't go through. ", refused.Why);
        Assert.Equal((false, (string?)null), await PeopleChanges.MakeAsync(En, "t", change, _ => Task.FromResult(StepConfirmed.Yes),
            _ => Task.FromResult(new byte[1]), (_, _) => Task.FromResult((204, "")), () => "x", () => false));
    }

    [Fact]
    public void Adding_someone_and_a_role_change_are_approvals_and_the_rest_are_not()
    {
        Assert.True(PeopleChanges.SetRole(En, Support, "sub_cy", "cy@x", "member", adding: true).Approval);
        Assert.True(PeopleChanges.SetRole(En, Support, "sub_ana", "ana@x", "manager", adding: false).Approval);
        Assert.True(PeopleChanges.SetRole(En, Support, "sub_ana", "ana@x", "member", adding: false).Approval);
        Assert.False(PeopleChanges.Create(En, "Help").Approval);
        Assert.False(PeopleChanges.Rename(En, Support, "Help").Approval);
        Assert.False(PeopleChanges.Delete(En, Support).Approval);
        Assert.False(PeopleChanges.Remove(En, Support, "sub_ana", "ana@x").Approval);
    }

    [Fact]
    public async Task An_approval_goes_with_the_passkey_alone_never_the_session_key()
    {
        var said = new List<string>();
        var change = PeopleChanges.SetRole(En, Support, "sub_cy", "cy@x", "member", adding: true);
        var (done, why) = await PeopleChanges.MakeAsync(En, "t_1", change,
            _ => { said.Add("confirm"); return Task.FromResult(StepConfirmed.Yes); },
            _ => { said.Add("device key"); return Task.FromResult(new byte[1]); },
            (c, h) => { said.Add($"send {c.Method} {c.Path} {h.Header} {h.Value}"); return Task.FromResult((200, "{}")); },
            () => "x", () => true,
            c => { said.Add("passkey " + c.What); return Task.FromResult(("KIFF-Passkey-Assertion", "t=x,a=y")); });
        Assert.Equal((true, (string?)null), (done, why));
        Assert.Equal(["passkey " + change.What, "send PUT /v1/groups/grp_1/members/sub_cy KIFF-Passkey-Assertion t=x,a=y"], said);
    }

    [Fact]
    public async Task An_approval_with_no_passkey_or_one_closed_or_refused_sends_nothing()
    {
        var sent = false;
        var change = PeopleChanges.SetRole(En, Support, "sub_cy", "cy@x", "manager", adding: true);
        Task<(int, string)> Send(PeopleChange c, (string, string) h) { sent = true; return Task.FromResult((200, "")); }
        Task<StepConfirmed> Yes(string _) => Task.FromResult(StepConfirmed.Yes);
        Task<byte[]> Sign(byte[] _) => Task.FromResult(new byte[1]);

        var none = await PeopleChanges.MakeAsync(En, "t", change, Yes, Sign, Send, () => "x", () => true);
        Assert.False(none.Done);
        Assert.EndsWith("Not sent: it wasn't confirmed.", none.Why);
        Assert.Equal((false, (string?)null), await PeopleChanges.MakeAsync(En, "t", change, Yes, Sign, Send, () => "x", () => true,
            _ => throw new OperationCanceledException()));
        var refused = await PeopleChanges.MakeAsync(En, "t", change, Yes, Sign, Send, () => "x", () => true,
            _ => throw new PasskeyRefusedException("user_verification"));
        Assert.EndsWith("Not sent: it wasn't confirmed.", refused.Why);
        Assert.False(sent);

        var notAllowed = await PeopleChanges.MakeAsync(En, "t", change, Yes, Sign, (_, _) => Task.FromResult((403, "{\"error\":\"passkey_required\"}")),
            () => "x", () => true, _ => Task.FromResult(("KIFF-Passkey-Assertion", "t=x")));
        Assert.Equal($"{change.What} didn't go through. {KiffErrors.Words(En, 403)}", notAllowed.Why);
    }
}

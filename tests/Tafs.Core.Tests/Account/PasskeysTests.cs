using System.Text.Json;
using Tafs.Core.Account;
using Tafs.Core.Approvals;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Account;

/// <summary>Account › Devices' passkeys, and signing a device out or removing a passkey from this PC (P11), as the iPad's DevicesView.</summary>
[Part("account")]
public class PasskeysTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    [Fact]
    public void Passkeys_read_with_a_computer_s_own_going_with_it()
    {
        var read = Passkeys.Read(Json("""
            {"passkeys": [{"id": "pk_1", "name": "iPhone", "synced": true, "created_at": "2026-11-16T10:00:00Z"},
                          {"id": "pk_2", "name": null, "device_id": "dev_9"},
                          {"name": "Old"}]}
            """))!;
        Assert.Equal([new AccountPasskey("pk_1", "iPhone", null), new AccountPasskey("pk_2", null, "dev_9"), new AccountPasskey(null, "Old", null)], read);
        Assert.Equal([true, false, false], read.Select(p => p.Removable));
        Assert.Equal("A device", Passkeys.Name(En, read[1]));
        Assert.Empty(Passkeys.Read(Json("{}"))!);
        Assert.Null(Passkeys.Read(Json("""{"passkeys": [{"id": 7}]}""")));
        Assert.Equal("/v1/me/passkeys/pk%2F1", Passkeys.RemovePath("pk/1"));
        Assert.Equal("/v1/me/devices/dev_9", Devices.DevicePath("dev_9"));
    }

    [Fact]
    public void Only_an_owner_or_admin_on_a_PC_that_approves_removes_a_passkey()
    {
        Assert.True(Passkeys.MayRemove("owner", approves: true));
        Assert.True(Passkeys.MayRemove("admin", approves: true));
        Assert.False(Passkeys.MayRemove("member", approves: true));
        Assert.False(Passkeys.MayRemove(null, approves: true));
        Assert.False(Passkeys.MayRemove("owner", approves: false));
        Assert.Equal("owner", KiffAccount.Read(Json("""{"tenant_id": "t", "tenant_slug": "s", "role": "owner"}"""))!.Role);
    }

    [Fact]
    public async Task A_removal_is_signed_then_sent_and_204_is_done()
    {
        var said = new List<string>();
        var (done, why) = await AccountRemoval.RemoveAsync(En, "/v1/me/devices/dev_9", "Sign Laptop out of your account",
            path => { said.Add("sign " + path); return Task.FromResult(("KIFF-Passkey-Assertion", "t=1,a=x")); },
            (path, signature) => { said.Add($"delete {path} {signature.Header}"); return Task.FromResult(new CloudRead(null, Status: 204)); },
            () => true);
        Assert.Equal((true, (string?)null), (done, why));
        Assert.Equal(["sign /v1/me/devices/dev_9", "delete /v1/me/devices/dev_9 KIFF-Passkey-Assertion"], said);
    }

    [Fact]
    public async Task Windows_Hello_closed_sends_nothing_and_a_refusal_says_why()
    {
        var sent = false;
        var closed = await AccountRemoval.RemoveAsync(En, "/p", "Remove the passkey iPhone from your account",
            _ => Task.FromException<(string, string)>(new OperationCanceledException()),
            (_, _) => { sent = true; return Task.FromResult(new CloudRead(null, Status: 204)); }, () => true);
        Assert.Equal((false, "Cancelled. Nothing changed."), closed);
        Assert.False(sent);

        var refused = await AccountRemoval.RemoveAsync(En, "/p", "Remove the passkey iPhone from your account",
            _ => Task.FromResult(("KIFF-Passkey-Assertion", "t=1,a=x")),
            (_, _) => Task.FromResult(new CloudRead(null, "This device can't approve.", 403)), () => true);
        Assert.Equal((false, "Remove the passkey iPhone from your account didn't go through. This device can't approve."), refused);

        // Signed out meanwhile: quietly nothing.
        Assert.Equal((false, (string?)null), await AccountRemoval.RemoveAsync(En, "/p", "x",
            _ => Task.FromException<(string, string)>(new PasskeyRefusedException("no")), (_, _) => Task.FromResult(new CloudRead(null, Status: 204)), () => false));
    }
}

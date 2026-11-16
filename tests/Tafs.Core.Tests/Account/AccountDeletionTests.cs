using System.Text.Json;
using Tafs.Core.Account;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Account;

/// <summary>Deleting the account, as the iPad's deleteAccount: Windows Hello, then tafs cloud, then planque, then KIFF, last.</summary>
[Part("account")]
public class AccountDeletionTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    sealed class Calls
    {
        public readonly List<string> Made = [];
        public StepConfirmed Confirmed = StepConfirmed.Yes;
        public CloudRead Cloud = CloudRead.Of(JsonDocument.Parse("""{"done":true}""").RootElement.Clone());
        public int Planque = 204, Kiff = 204;
        public byte[]? Signed;
        public bool Current = true;

        public Task<(Deleted, string?)> Delete() => AccountDeletion.DeleteAsync(En, "t_01",
            p => { Made.Add("confirm " + p); return Task.FromResult(Confirmed); },
            () => { Made.Add("cloud"); return Task.FromResult(Cloud); },
            () => { Made.Add("planque"); return Task.FromResult(Planque); },
            h => { Made.Add("kiff " + h); return Task.FromResult(Kiff); },
            m => { Signed = m; return Task.FromResult(new byte[] { 1, 2 }); },
            () => "2026-11-16T12:40:00.000Z", () => Current);
    }

    /// <summary>In the iPad's order, KIFF last over its exact signed request; deleted, the session ends.</summary>
    [Fact]
    public async Task It_deletes_in_order_with_KIFF_last()
    {
        var c = new Calls();
        Assert.Equal((Deleted.Yes, (string?)null), await c.Delete());
        Assert.Equal(["confirm Delete your account", "cloud", "planque", "kiff t=2026-11-16T12:40:00.000Z,v1=AQI="], c.Made);
        Assert.Equal(KiffSigning.RequestV1("t_01", "POST", "/v1/me/delete", AccountDeletion.Body(), "2026-11-16T12:40:00.000Z"), c.Signed);
        Assert.Equal("/v1/accounts/t_01", AccountDeletion.PlanquePath("t_01"));
    }

    /// <summary>Each step that fails says which part is left, and nothing after it is asked.</summary>
    [Fact]
    public async Task Each_step_that_fails_says_what_is_left()
    {
        var closed = new Calls { Confirmed = StepConfirmed.Cancelled };
        Assert.Equal((Deleted.No, En.Get("common.not_sent_unconfirmed")), await closed.Delete());
        Assert.Single(closed.Made);

        var owner = new Calls { Cloud = new CloudRead(null, "no", 403) };
        Assert.Equal((Deleted.No, "Only the account's owner can delete it."), await owner.Delete());
        Assert.DoesNotContain("planque", owner.Made);

        var notYet = new Calls { Cloud = CloudRead.Of(JsonDocument.Parse("""{"done":false,"failed":"runs"}""").RootElement.Clone()) };
        Assert.Equal(En.Get("settings.delete_account.not_erased_yet"), (await notYet.Delete()).Item2);

        var planque = new Calls { Planque = 503 };
        Assert.Equal($"Your tafs are erased, but your connections aren't yet. {En.Get("error.down")}", (await planque.Delete()).Item2);
        Assert.DoesNotContain(planque.Made, m => m.StartsWith("kiff", StringComparison.Ordinal));

        var kiff = new Calls { Kiff = 403 };
        Assert.Equal($"Your tafs and connections are erased, but KIFF didn't delete the account. {En.Get("error.not_allowed")}", (await kiff.Delete()).Item2);
    }

    [Fact]
    public async Task A_sign_out_meanwhile_stops_it()
    {
        var c = new Calls { Current = false };
        Assert.Equal((Deleted.Stopped, (string?)null), await c.Delete());
        Assert.Single(c.Made);
    }

    [Fact]
    public void Delete_is_offered_by_KIFF_s_account_delete_or_when_none_are_listed()
    {
        Assert.True(new KiffAccount("t", "s", null, null, null, ["account.delete"]).MayDelete);
        Assert.False(new KiffAccount("t", "s", null, null, null, ["cards.manage"]).MayDelete);
        Assert.True(new KiffAccount("t", "s", null, null, null).MayDelete);
    }
}

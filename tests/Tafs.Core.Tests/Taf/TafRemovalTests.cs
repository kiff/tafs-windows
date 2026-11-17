using System.Text.Json;
using Tafs.Core.Contracts;
using Tafs.Core.Home;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>Remove for good, as the iPad's <c>removeTaf</c>: signed first, its Cards revoked, then tafs cloud's DELETE.</summary>
[Part("record")]
public class TafRemovalTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static CloudRead Answer(string json) => CloudRead.Of(JsonDocument.Parse(json).RootElement);

    sealed class Calls
    {
        public List<string> Said { get; } = [];
        public Func<string, bool> Revokes { get; set; } = _ => true;
        public Func<CloudRead> Delete { get; set; } = () => Answer("""{"done": true, "removed": ["schedule", "history", "memory", "key", "name", "connections"], "failed": []}""");
        public Exception? SignFails { get; set; }
        public bool Current { get; set; } = true;

        public Task<RemovalResult> Run(bool cardsRevoked = false) => TafRemoval.RemoveAsync(En, "taf/1", "Rae", ["c_1", "c_2", "c_1"], cardsRevoked,
            path =>
            {
                Said.Add("sign " + path);
                return SignFails is { } e ? Task.FromException<(string, string)>(e) : Task.FromResult(("KIFF-Passkey-Assertion", "t=1,a=x"));
            },
            path => { Said.Add("revoke " + path); return Task.FromResult(Revokes(path)); },
            (path, signature) => { Said.Add($"delete {path} {signature.Header}"); return Task.FromResult(Delete()); },
            () => Current);
    }

    [Fact]
    public async Task Signed_first_then_its_Cards_revoked_then_tafs_cloud_removes_it()
    {
        var calls = new Calls();
        var done = await calls.Run();
        Assert.True(done.Done);
        Assert.Equal("Its Cards are revoked in KIFF. Its access to apps is removed. Its schedule, chats, run reports, memory and key are deleted, and the name Rae is free. KIFF's receipts stay.", done.Words);
        Assert.Equal(["sign /v1/tafs/taf%2F1", "revoke /v1/me/mandates/c_1/revoke", "revoke /v1/me/mandates/c_2/revoke", "delete /v1/tafs/taf%2F1 KIFF-Passkey-Assertion"], calls.Said);
        Assert.Empty(TafRemoval.Body());
    }

    [Fact]
    public async Task Hello_closed_sends_nothing_and_a_revoked_taf_revokes_nothing()
    {
        var closed = new Calls { SignFails = new OperationCanceledException() };
        Assert.Equal(new RemovalResult(false, "Not removed. Nothing changed."), await closed.Run());
        Assert.Single(closed.Said);

        var revoked = new Calls();
        await revoked.Run(cardsRevoked: true);
        Assert.DoesNotContain(revoked.Said, s => s.StartsWith("revoke", StringComparison.Ordinal));
    }

    [Fact]
    public async Task What_s_left_is_said_and_tafs_cloud_not_answering_says_why()
    {
        var partly = new Calls
        {
            Revokes = p => !p.Contains("c_2"),
            Delete = () => Answer("""{"done": false, "removed": ["schedule"], "failed": ["memory", "key", "odd"]}"""),
        };
        Assert.Equal(new RemovalResult(false,
            "KIFF didn't revoke 1 of its Cards: revoke them in KIFF. Not finished: its memory, key, odd couldn't be removed. tafs tries again each time it refreshes."),
            await partly.Run());

        var down = new Calls { Delete = () => CloudRead.Refused(En, 502, """{"error": "Bad gateway"}""") };
        var r = await down.Run();
        Assert.False(r.Done);
        Assert.Equal("Its Cards are revoked in KIFF. tafs cloud didn't answer (Bad gateway). If it began, tafs finishes it the next time it refreshes; if not, tap Remove again.", r.Words);

        var gone = new Calls();
        gone.Revokes = _ => { gone.Current = false; return true; };
        Assert.Equal(new RemovalResult(false, null), await gone.Run());
        Assert.DoesNotContain(gone.Said, s => s.StartsWith("delete", StringComparison.Ordinal));
    }

    [Fact]
    public void The_confirmation_says_what_goes_in_tafs_cloud_s_words()
    {
        Assert.Equal("Qonto goes with it.", TafRemoval.PreviewWords(JsonDocument.Parse("""{"words": " Qonto goes with it. "}""").RootElement));
        Assert.Null(TafRemoval.PreviewWords(JsonDocument.Parse("""{"words": "  "}""").RootElement));
        Assert.Equal(En.Get("taf.remove_body") + "\n\nQonto goes with it.", TafRemoval.ConfirmText(En, "Qonto goes with it."));
        Assert.Equal(En.Get("taf.remove_body"), TafRemoval.ConfirmText(En, null));
        Assert.Null(TafRemoval.Read(JsonDocument.Parse("""{"removed": []}""").RootElement));
    }

    /// <summary>Home's taf keeps its Cards' ids (<c>card.ids</c>), to revoke before it's removed.</summary>
    [Fact]
    public void Home_keeps_a_taf_s_Card_ids()
    {
        var home = HomeList.Read(En, ContractFile.Answer(SharedFiles.Contract("home.json")), TimeZoneInfo.Utc);
        Assert.Equal(["card_rae"], home.Tafs[0].CardIds);
    }

    [Fact]
    public async Task Revoke_revokes_each_Card_and_stops_at_the_first_KIFF_refuses()
    {
        var asked = new List<string>();
        Task<CloudRead> Ok(string path) { asked.Add(path); return Task.FromResult(CloudRead.Of(System.Text.Json.JsonDocument.Parse("{}").RootElement)); }
        Assert.Equal((null, 2), await TafRemoval.RevokeCardsAsync(En, ["crd_1", "crd_2"], Ok));
        Assert.Equal(["/v1/me/mandates/crd_1/revoke", "/v1/me/mandates/crd_2/revoke"], asked);

        asked.Clear();
        Task<CloudRead> SecondRefused(string path)
        {
            asked.Add(path);
            return Task.FromResult(asked.Count == 1 ? CloudRead.Of(System.Text.Json.JsonDocument.Parse("{}").RootElement) : new CloudRead(null, "Too busy.", 429));
        }
        // The first was revoked before the second was refused: counted, so Home is read back.
        Assert.Equal(("KIFF didn't revoke the Card. Too busy.", 1), await TafRemoval.RevokeCardsAsync(En, ["crd_1", "crd_2", "crd_3"], SecondRefused));
        Assert.Equal(2, asked.Count);
        Assert.Equal(("KIFF didn't revoke the Card. It didn't reach the server. Try again once you're online.", 0),
            await TafRemoval.RevokeCardsAsync(En, ["crd_1"], _ => throw new HttpRequestException("down")));
    }
}

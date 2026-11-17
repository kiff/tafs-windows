using Tafs.Core.Home;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Home;

/// <summary>Discard of a taf being set up, as the iPad's <c>discardSettingUp</c>: signed first, its Cards revoked, then tafs cloud.</summary>
[Part("home")]
public class SetupDiscardTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static readonly SettingUp Invoices = new("Invoices", "agt/inv", 3, null,
        new DiscardWords("Discard", "Discard Invoices?", "", "Discard", "Keep"), ["c_1", "c_2"]);

    sealed class Calls
    {
        public List<string> Said { get; } = [];
        public Func<string, bool> Revokes { get; set; } = _ => true;
        public Func<CloudRead> Answer { get; set; } = () => CloudRead.Of(System.Text.Json.JsonDocument.Parse("""{"discarded": true}""").RootElement);
        public Exception? SignFails { get; set; }
        public bool Current { get; set; } = true;

        public Task<DiscardResult> Run(SettingUp taf, IReadOnlyList<string>? left = null) => SetupDiscard.DiscardAsync(En, taf, left ?? [],
            path =>
            {
                Said.Add("sign " + path);
                return SignFails is { } e ? Task.FromException<(string, string)>(e) : Task.FromResult(("KIFF-Passkey-Assertion", "t=1,a=x"));
            },
            path => { Said.Add("revoke " + path); return Task.FromResult(Revokes(path)); },
            (path, signature) => { Said.Add($"post {path} {signature.Header}"); return Task.FromResult(Answer()); },
            () => Current);
    }

    [Fact]
    public async Task Signed_first_then_each_Card_revoked_then_tafs_cloud_asked()
    {
        var calls = new Calls();
        var done = (await calls.Run(Invoices, ["c_2", "c_3"]));
        Assert.Equal(new DiscardResult(true, null), done);
        Assert.Equal([
            "sign /v1/home/setting-up/agt%2Finv/discard",
            "revoke /v1/me/mandates/c_1/revoke", "revoke /v1/me/mandates/c_2/revoke", "revoke /v1/me/mandates/c_3/revoke",
            "post /v1/home/setting-up/agt%2Finv/discard KIFF-Passkey-Assertion"], calls.Said);
        Assert.Empty(SetupDiscard.Body());
    }

    [Fact]
    public async Task Hello_closed_or_refused_sends_nothing()
    {
        foreach (var e in new Exception[] { new OperationCanceledException(), new Tafs.Core.Approvals.PasskeyRefusedException("no"), new InvalidOperationException() })
        {
            var calls = new Calls { SignFails = e };
            Assert.Equal(new DiscardResult(false, "Not removed. Nothing changed."), (await calls.Run(Invoices)));
            Assert.Single(calls.Said);
        }
    }

    [Fact]
    public async Task Cards_still_in_force_are_named_for_the_next_Discard()
    {
        var calls = new Calls
        {
            Revokes = p => !p.Contains("c_2"),
            Answer = () => CloudRead.Refused(En, 409, """{"error": "Cards in force", "code": "cards_in_force", "cards": ["c_2", 7]}"""),
        };
        var refused = (await calls.Run(Invoices));
        Assert.False(refused.Done);
        Assert.Equal("KIFF didn't revoke 1 of its Cards: revoke them in KIFF.", refused.Why);
        Assert.Equal(["c_2"], refused.CardsLeft);

        // None named: as many as KIFF didn't revoke here, at least one.
        calls.Answer = () => CloudRead.Refused(En, 409, """{"code": "cards_in_force"}""");
        calls.Revokes = _ => false;
        Assert.Equal("KIFF didn't revoke 2 of its Cards: revoke them in KIFF.", (await calls.Run(Invoices)).Why);
    }

    [Fact]
    public async Task Any_other_refusal_says_tafs_clouds_words_and_a_sign_out_says_nothing()
    {
        var calls = new Calls { Answer = () => CloudRead.Refused(En, 404, """{"error": "Not a taf being set up of yours"}""") };
        Assert.Equal(new DiscardResult(false, "Not a taf being set up of yours"), (await calls.Run(Invoices)));

        var gone = new Calls();
        gone.Revokes = _ => { gone.Current = false; return true; };
        Assert.Equal(new DiscardResult(false, null), (await gone.Run(Invoices)));
        Assert.DoesNotContain(gone.Said, s => s.StartsWith("post", StringComparison.Ordinal));

        // No agent or no Discard words: nothing is asked.
        var none = new Calls();
        Assert.Equal(new DiscardResult(false, null), (await none.Run(Invoices with { Agent = null })));
        Assert.Equal(new DiscardResult(false, null), (await none.Run(Invoices with { Discard = null })));
        Assert.Empty(none.Said);
    }
}

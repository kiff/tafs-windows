using System.Text;
using System.Text.Json;
using Tafs.Core.Account;
using Tafs.Core.Kiff;
using Tafs.Core.NeedsYou;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Account;

/// <summary>Sharing a taf from this PC (P32), as the iPad's ShareTafSheet.</summary>
[Part("account")]
public class SharesTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static readonly PeopleGroup Support = new("grp_1", "Support", null, [new("sub_ana", "manager"), new("sub_bo", "member")], []);
    static readonly PeopleGroup Everyone = new("grp_0", "Everyone", true, [new("sub_ana", "member")], []);
    static readonly Person Ana = new("sub_ana", "member", "active", "ana@x.test");
    static readonly Person Bo = new("sub_bo", "member", "active", "bo@x.test");
    static Person? Find(string s) => new[] { Ana, Bo }.FirstOrDefault(p => p.Subject == s);

    [Fact]
    public void Shares_read_and_their_routes()
    {
        var read = Shares.Read(JsonDocument.Parse("""{"grants": [{"group": "grp_1"}, {"subject": "sub_bo"}]}""").RootElement)!;
        Assert.Equal([new TafShare("grp_1", null), new TafShare(null, "sub_bo")], read);
        Assert.Null(Shares.Read(JsonDocument.Parse("""{"grants": [{}]}""").RootElement));
        Assert.Equal("/v1/agents/refunds-k3/grants", Shares.Path("refunds-k3"));
        Assert.Equal("/v1/agents/a/grants?group=grp_1", Shares.RemovePath("a", new("grp_1", null)));
        Assert.Equal("/v1/agents/a/grants?subject=sub%7Cbo", Shares.RemovePath("a", new(null, "sub|bo")));
        Assert.Equal("""{"group":"grp_1"}""", Encoding.UTF8.GetString(Shares.AddBody(new("grp_1", null))));
        Assert.Equal("""{"subject":"sub_bo"}""", Encoding.UTF8.GetString(Shares.AddBody(new(null, "sub_bo"))));
    }

    [Fact]
    public void What_it_means_in_the_app_s_words()
    {
        Assert.Equal("ana@x.test (manager), bo@x.test", Shares.PeopleOf(En, Support, Find));
        var lines = Shares.Consequences(En, "Refunds", [new(null, "sub_bo")], [new("grp_1", null), new("grp_0", null)], [Support, Everyone], Find);
        Assert.Equal([
            En.Get("people.own_chats"),
            "Everyone uses Refunds: ana@x.test.",
            "Support uses Refunds: ana@x.test (manager), bo@x.test.",
            "bo@x.test stops using Refunds. Their chats stay theirs.",
        ], lines);
        Assert.Equal(["Support stops using Refunds. Their chats stay theirs."],
            Shares.Consequences(En, "Refunds", [new("grp_1", null)], [], [Support], Find));
    }

    sealed class Calls
    {
        public readonly List<string> Said = [];
        public StepConfirmed Confirmed = StepConfirmed.Yes;
        public int Status = 204;

        public Task<(bool, string?)> Apply(IReadOnlyCollection<TafShare> add, IReadOnlyCollection<TafShare> remove, bool current = true) =>
            Shares.ApplyAsync(En, "t_1", "Refunds", "refunds-k3", add, remove,
                (path, body) => { Said.Add($"passkey POST {path} {Encoding.UTF8.GetString(body)}"); return Task.FromResult(("KIFF-Passkey-Assertion", "t=1,a=x")); },
                prompt => { Said.Add("hello " + prompt); return Task.FromResult(Confirmed); },
                message => { Said.Add("tpm"); return Task.FromResult(new byte[] { 1 }); },
                () => "2026-11-17T07:00:00.000Z",
                (method, path, body, signature) => { Said.Add($"send {method} {path} {signature.Header}"); return Task.FromResult((Status, "")); },
                () => current);
    }

    [Fact]
    public async Task New_shares_are_approved_by_the_passkey_then_removals_signed_by_the_key()
    {
        var calls = new Calls();
        Assert.Equal((true, (string?)null), await calls.Apply([new("grp_1", null)], [new(null, "sub_bo")]));
        Assert.Equal([
            """passkey POST /v1/agents/refunds-k3/grants {"group":"grp_1"}""",
            "send POST /v1/agents/refunds-k3/grants KIFF-Passkey-Assertion",
            "tpm",
            "send DELETE /v1/agents/refunds-k3/grants?subject=sub_bo KIFF-Device-Signature",
        ], calls.Said);
    }

    [Fact]
    public async Task Only_taking_away_is_confirmed_once_first_and_closed_sends_nothing()
    {
        var calls = new Calls();
        Assert.True((await calls.Apply([], [new("grp_1", null)])).Item1);
        Assert.Equal(["hello Share Refunds", "tpm", "send DELETE /v1/agents/refunds-k3/grants?group=grp_1 KIFF-Device-Signature"], calls.Said);

        var closed = new Calls { Confirmed = StepConfirmed.Cancelled };
        Assert.Equal((false, (string?)null), await closed.Apply([], [new("grp_1", null)]));
        Assert.Equal(["hello Share Refunds"], closed.Said);

        var refused = new Calls { Status = 403 };
        var (done, why) = await refused.Apply([new(null, "sub_bo")], []);
        Assert.False(done);
        Assert.StartsWith("Share Refunds didn't go through. ", why);
    }

    [Fact]
    public async Task A_share_that_names_no_one_is_never_signed_or_sent()
    {
        Assert.Throws<ArgumentException>(() => Shares.RemovePath("a", new(null, null)));
        Assert.Throws<ArgumentException>(() => Shares.RemovePath("a", new("", null)));
        // An empty group beside a person is that person's share, by its person.
        Assert.Equal("/v1/agents/a/grants?subject=sub_bo", Shares.RemovePath("a", new("", "sub_bo")));
        Assert.Equal("""{"subject":"sub_bo"}""", System.Text.Encoding.UTF8.GetString(Shares.AddBody(new("", "sub_bo"))));
        foreach (var odd in new TafShare[] { new(null, null), new("", ""), new("grp_1", "sub_bo") })
        {
            var removing = new Calls();
            var (done, why) = await removing.Apply([], [odd]);
            Assert.False(done);
            Assert.StartsWith("Share Refunds didn't go through. ", why);
            Assert.Empty(removing.Said);
            var adding = new Calls();
            Assert.False((await adding.Apply([odd], [new("grp_1", null)])).Item1);
            Assert.Empty(adding.Said);
        }
    }

    [Fact]
    public void Who_uses_it_under_its_name_never_naming_the_reader()
    {
        string Line(IReadOnlyList<TafShare>? shares) =>
            Shares.SharedWith(En, shares, [Support, Everyone], s => s == "sub_ana", s => Find(s)?.Label ?? s);
        Assert.Equal("Sharing…", Line(null));
        Assert.Equal("Not shared", Line([]));
        Assert.Equal("Shared with everyone", Line([new("grp_0", null)]));
        Assert.Equal("Shared with Support and bo@x.test", Line([new("grp_1", null), new(null, "sub_bo")]));
        // Only the reader: not "Shared with" themselves.
        Assert.Equal("Not shared", Line([new(null, "sub_ana")]));
        Assert.Equal("Shared with bo@x.test", Line([new(null, "sub_ana"), new(null, "sub_bo")]));
    }

    [Fact]
    public void No_line_unless_every_read_answered_and_the_reader_is_known_by_email_too()
    {
        IReadOnlyList<TafShare> toSupport = [new("grp_1", null)];
        // Groups unread: not "Not shared".
        Assert.Null(Shares.SharedWithLine(En, toSupport, null, [Ana, Bo], null));
        // People unread: never the reader by id.
        Assert.Null(Shares.SharedWithLine(En, [new(null, "sub_ana")], [Support], null, null));
        Assert.Null(Shares.SharedWithLine(En, null, [Support], [Ana, Bo], null));
        Assert.Equal("Shared with Support", Shares.SharedWithLine(En, toSupport, [Support, Everyone], [Ana, Bo], null));
        // The reader by their email, found or not: as the iPad's.
        Assert.Equal("Not shared", Shares.SharedWithLine(En, [new(null, "sub_ana")], [Support], [Ana with { You = null }, Bo], "ANA@x.test"));
        Assert.Equal("Not shared", Shares.SharedWithLine(En, [new(null, "me@x.test")], [Support], [Ana, Bo], "me@x.test"));
        Assert.Equal("Shared with bo@x.test", Shares.SharedWithLine(En, [new(null, "sub_bo")], [Support], [Ana with { You = null }, Bo], "ana@x.test"));
    }
}

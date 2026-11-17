using System.Text;
using Tafs.Core.Connections;
using Tafs.Core.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Connections;

/// <summary>Approving a connection on this PC (P9): only as shown, in KIFF's approval window, then the vendor's sign-in and planque's confirm.</summary>
[Part("connections")]
public class ConnectFlowTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    const string Win = "win_abcdefghijklmnopqrstuvwxyz";
    const string Win2 = "win_bbcdefghijklmnopqrstuvwxyz";

    static PlanqueGrant Grant(string status = "pending", bool matches = true, string digest = "d1") =>
        new("grt_1", status, "Gmail: read and send mail", digest, new GrantRequest("gmail", "rae-k3f9", []), []) { DigestMatches = matches, VendorName = "Gmail" };

    sealed class Calls
    {
        public List<string> Said { get; } = [];
        public List<PlanqueGrant> Now { get; set; } = [Grant()];
        public List<PlanqueGrant> After { get; set; } = [Grant("connected")];
        public OpenWindow? Read { get; set; }
        public bool HelloClosed { get; set; }
        public Queue<(int, string)> Taps { get; } = new();
        public string? Code { get; set; } = "c_42";
        int reads;

        public ConnectCalls Make() => new(
            () => Task.FromResult<IReadOnlyList<PlanqueGrant>?>(reads++ == 0 ? Now : After),
            () => { Said.Add("read window"); return Task.FromResult(Read); },
            () =>
            {
                Said.Add("hello");
                if (HelloClosed) throw new OperationCanceledException();
                var id = Read is null ? Win : Win2;
                return Task.FromResult<(OpenWindow?, string?)>((new OpenWindow(id, DateTimeOffset.UtcNow.AddMinutes(5), true, "device", null), null));
            },
            (window, path, body) => { Said.Add($"tap {window[..5]}{window[^1]} {path} {Encoding.UTF8.GetString(body)}"); return Task.FromResult(Taps.Dequeue()); },
            (page, grant) => { Said.Add($"sign in {page.Host} {grant}"); return Task.FromResult(Code); },
            () => true, e => e.Message);
    }

    [Fact]
    public async Task Approved_in_the_window_then_signed_in_then_confirmed()
    {
        var calls = new Calls();
        calls.Taps.Enqueue((200, """{"status": "approved", "sign_in_url": "https://accounts.google.com/o?x=1"}"""));
        calls.Taps.Enqueue((200, """{"id": "grt_1", "status": "connected"}"""));
        Assert.Equal(new ConnectResult(true), await ConnectFlow.ApproveAsync(En, "t_1", "grt_1", "d1", null, calls.Make()));
        Assert.Equal([
            "read window", "hello",
            """tap win_az /v1/accounts/t_1/grants/grt_1/approve {"digest":"d1"}""",
            "sign in accounts.google.com grt_1",
            """tap win_az /v1/accounts/t_1/grants/grt_1/confirm {"confirm":"c_42"}""",
        ], calls.Said);
    }

    [Fact]
    public async Task An_open_window_is_used_with_no_Windows_Hello_and_one_that_ended_is_opened_again_once()
    {
        var calls = new Calls { Read = new OpenWindow(Win, DateTimeOffset.UtcNow.AddMinutes(4), true, "device", null) };
        calls.Taps.Enqueue((403, """{"error": "window_required"}"""));
        calls.Taps.Enqueue((200, """{"status": "signed_in", "confirm": "c_7"}"""));
        calls.Taps.Enqueue((200, """{"status": "connected"}"""));
        Assert.True((await ConnectFlow.ApproveAsync(En, "t_1", "grt_1", "d1", null, calls.Make())).Connected);
        Assert.Equal(1, calls.Said.Count(s => s == "hello"));
        Assert.Contains("""tap win_bz /v1/accounts/t_1/grants/grt_1/confirm {"confirm":"c_7"}""", calls.Said);
        Assert.DoesNotContain(calls.Said, s => s.StartsWith("sign in", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_what_was_shown_is_approved()
    {
        var changed = new Calls { Now = [Grant(digest: "d2")] };
        var result = await ConnectFlow.ApproveAsync(En, "t_1", "grt_1", "d1", null, changed.Make());
        Assert.Equal("d2", result.Changed!.Digest);
        Assert.Empty(changed.Said);

        var mismatch = new Calls { Now = [Grant(matches: false)] };
        Assert.Equal("This request doesn't match what it asks for. Nothing was approved.",
            (await ConnectFlow.ApproveAsync(En, "t_1", "grt_1", "d1", null, mismatch.Make())).Why);

        var withProfile = new Calls { Now = [Grant() with { Profile = new JcsValue.Text("p") }] };
        Assert.Equal("KIFF's words for this connection aren't read yet. Nothing was approved.",
            (await ConnectFlow.ApproveAsync(En, "t_1", "grt_1", "d1", null, withProfile.Make())).Why);
        Assert.Empty(withProfile.Said);

        var failed = new Calls { Now = [Grant("failed") with { Reason = "the vendor refused" }] };
        var no = await ConnectFlow.ApproveAsync(En, "t_1", "grt_1", "d1", null, failed.Make());
        Assert.True(no.NotApprovable);
        Assert.Equal("This connection is failed: the vendor refused", no.Why);
    }

    [Fact]
    public async Task Windows_Hello_closed_or_the_sign_in_left_is_nothing_said()
    {
        var closed = new Calls { HelloClosed = true };
        Assert.Equal(new ConnectResult(false), await ConnectFlow.ApproveAsync(En, "t_1", "grt_1", "d1", null, closed.Make()));
        Assert.DoesNotContain(closed.Said, s => s.StartsWith("tap", StringComparison.Ordinal));

        var left = new Calls { Code = null };
        left.Taps.Enqueue((200, """{"status": "approved", "sign_in_url": "https://accounts.google.com/o"}"""));
        Assert.Equal(new ConnectResult(false), await ConnectFlow.ApproveAsync(En, "t_1", "grt_1", "d1", null, left.Make()));
        Assert.DoesNotContain(left.Said, s => s.Contains("/confirm", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Connected_only_once_planque_says_so()
    {
        var calls = new Calls { After = [Grant("approved")] };
        calls.Taps.Enqueue((200, """{"status": "signed_in", "confirm": "c_7"}"""));
        calls.Taps.Enqueue((200, """{"status": "approved"}"""));
        Assert.Equal("The sign-in at Gmail didn't finish, so nothing was connected. Try signing in again.",
            (await ConnectFlow.ApproveAsync(En, "t_1", "grt_1", "d1", null, calls.Make())).Why);
    }

    [Fact]
    public void KIFF_s_words_are_shown_only_for_exactly_the_profile()
    {
        var profile = Jcs.Parse("""{"server":"gmail","ops":["send"]}"""u8.ToArray());
        var words = """{"v":"kiff.words.v1","lines":[{"op":"send","kind":"write","text":"It can send mail as you."}]}""";
        var wordsSha = ProfileWords.Digest(Jcs.Parse(Encoding.UTF8.GetBytes(words)));
        var answer = Encoding.UTF8.GetBytes($$"""{"profile_sha256":"{{ProfileWords.Digest(profile)}}","words_sha256":"{{wordsSha}}","words":{{words}}}""");
        var (read, why) = ProfileWords.Checked(En, answer, profile);
        Assert.Null(why);
        Assert.Equal("It can send mail as you.", read!.Lines.Single().Text);
        Assert.True(read.For(profile));
        var other = Jcs.Parse("""{"server":"gmail","ops":["read"]}"""u8.ToArray());
        Assert.Equal("These words are for another connection than the one shown. Nothing can be approved.", ProfileWords.Checked(En, answer, other).Why);
        var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(answer).Replace("as you", "as anyone"));
        Assert.Equal("KIFF's words don't match their check. Nothing can be approved.", ProfileWords.Checked(En, tampered, profile).Why);
    }

    [Fact]
    public void Which_taf_lists_the_tafs_that_need_it_and_the_start_names_its_grant()
    {
        IReadOnlyList<(string, string)> mine = [("t1", "Rae"), ("t2", "Ana"), ("t3", "Bo")];
        var list = ConnectAppsScreen.Read("""
            {"apps": [{"entry": "gmail", "title": "Gmail", "needed_by": [{"taf": "t2", "name": "Ana"}, {"taf": "t9", "name": "Gone"}, {"name": "no id"}]},
                      {"entry": "slack", "title": "Slack"}]}
            """);
        Assert.Equal([("t2", "Ana")], ConnectAppsScreen.ConnectableTafs(mine, list.Apps[0]));
        Assert.Equal(mine, ConnectAppsScreen.ConnectableTafs(mine, list.Apps[1]));
        // No taf needs it: Ask Coach. tafs cloud not saying, and no taf at all: nothing.
        Assert.Equal(ConnectRoute.AskCoach, ConnectAppsScreen.Route(list.Apps[0], []));
        Assert.Equal(ConnectRoute.NoTaf, ConnectAppsScreen.Route(list.Apps[1], []));
        Assert.Equal(ConnectRoute.Ask, ConnectAppsScreen.Route(list.Apps[1], mine));
        Assert.Equal("/v1/connect/apps/connector%3Acomposio%3Agmail", ConnectAppsScreen.StartPath("connector:composio:gmail").Replace(":", "%3A"));
        Assert.Equal("""{"taf":"t2"}""", Encoding.UTF8.GetString(ConnectAppsScreen.StartBody("t2")));
        Assert.Equal(("grt_1", false), ConnectAppsScreen.Started(System.Text.Json.JsonDocument.Parse("""{"grant": "grt_1"}""").RootElement));
        Assert.Equal(("grt_2", true), ConnectAppsScreen.Started(System.Text.Json.JsonDocument.Parse("""{"grant": {"id": "grt_2"}, "needs_key": true}""").RootElement));
        Assert.Null(ConnectAppsScreen.Started(System.Text.Json.JsonDocument.Parse("""{"grant": ""}""").RootElement));
    }
}

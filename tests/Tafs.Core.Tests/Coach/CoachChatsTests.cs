using System.Text;
using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>
/// Coach's conversations, as the iPad's SetupChatsList, ArchivedSetupChats, newSetupChat, openSetupChat and setupTarget; and
/// the panel's width, as its CoachPanel handle.
/// </summary>
[Part("coach")]
public class CoachChatsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    const string Chats = """
        {"chats": [
          {"session": "old", "title": "Set up refunds", "started": "2026-11-14T08:00:00Z", "updated": "2026-11-14T09:00:00Z"},
          {"session": "new", "title": "Connect Shippo", "started": "2026-11-15T08:00:00Z", "updated": "2026-11-15T09:00:00Z"}]}
        """;

    [Fact]
    public void The_list_is_newest_first_and_one_it_can_t_read_fails_it()
    {
        var rows = CoachChats.Read(Json(Chats))!;
        Assert.Equal(["new", "old"], rows.Select(r => r.Session));
        Assert.Equal("Connect Shippo", rows[0].Title);
        Assert.Null(CoachChats.Read(Json("""{"chats": [{"session": "x", "title": "t", "updated": "2026-11-15T09:00:00Z"}]}""")));
        Assert.Null(CoachChats.Read(Json("""{"nothing": []}""")));
        Assert.Empty(CoachChats.Read(Json("""{"chats": []}"""))!);
    }

    [Fact]
    public void Each_says_when_it_was_last_updated_in_this_PC_s_zone() =>
        Assert.Equal("15 Nov 2026, 10:00", CoachChats.When(En, DateTimeOffset.Parse("2026-11-15T09:00:00Z"),
            TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Paris")));

    [Fact]
    public async Task A_list_unread_says_why()
    {
        var (rows, why) = await CoachChats.ReadAsync(En, "/v1/tafs/c/chats", _ => Task.FromResult(CloudRead.Refused(En, 503, """{"words": "Try again in a minute."}""")));
        Assert.Null(rows);
        Assert.Equal("The conversation couldn't be loaded: Try again in a minute.", why);
    }

    /// <summary>Archive and Put back, on tafs cloud's routes; not taken, tafs cloud's words, else the try-again line.</summary>
    [Fact]
    public async Task Archive_and_put_back_go_to_tafs_cloud_and_say_why_not()
    {
        var asked = new List<string>();
        Task<CloudRead> Taken(string path, byte[] body) { asked.Add(path); return Task.FromResult(CloudRead.Of(Json("""{"archived": true}"""))); }
        Assert.Null(await CoachChats.ArchiveAsync(En, "c/1", "s 1", Taken));
        Assert.Null(await CoachChats.PutBackAsync(En, "c/1", "s 1", Taken));
        Assert.Equal(["/v1/tafs/c%2F1/chats/s%201/archive", "/v1/tafs/c%2F1/chats/s%201/unarchive"], asked);
        Assert.Equal("/v1/tafs/c%2F1/chats/archived", CoachChats.ArchivedPath("c/1"));

        Task<CloudRead> Refused(string path, byte[] body) => Task.FromResult(CloudRead.Refused(En, 503, """{"words": "Try again in a minute."}"""));
        Assert.Equal("Not archived. Try again in a minute.", await CoachChats.ArchiveAsync(En, "c", "s", Refused));
        Assert.Equal("Not put back. Try again in a minute.", await CoachChats.PutBackAsync(En, "c", "s", Refused));

        Task<CloudRead> Odd(string path, byte[] body) => Task.FromResult(CloudRead.Of(Json("""{"ok": 1}""")));
        Assert.Equal(En.Get("coach.not_archived_try_again"), await CoachChats.ArchiveAsync(En, "c", "s", Odd));
        Assert.Equal(En.Get("coach.not_put_back_try_again"), await CoachChats.PutBackAsync(En, "c", "s", Odd));
    }

    /// <summary>A reopened conversation is read by its session with no list; a new one reads nothing past Coach's id.</summary>
    [Fact]
    public async Task A_chosen_conversation_is_read_alone_and_a_new_one_not_at_all()
    {
        var asked = new List<string>();
        Task<CloudRead> Read(string path)
        {
            asked.Add(path);
            return Task.FromResult(CloudRead.Of(Json(path switch
            {
                "/v1/setup" => """{"setup": {"id": "c"}}""",
                "/v1/tafs/c/chats" => Chats,
                _ => """{"messages": [{"at": "2026-11-15T09:00:00Z", "from": "owner", "text": "Hi", "session": "old"}]}""",
            })));
        }

        var reopened = await CoachChat.LoadAsync(En, Read, CoachConversation.Of("old"));
        Assert.Equal(["/v1/setup", "/v1/tafs/c/conversation/old"], asked);
        Assert.Equal(("c", "old", 1), (reopened.Coach, reopened.Session, reopened.Thread!.Messages.Count));

        asked.Clear();
        var newest = await CoachChat.LoadAsync(En, Read, CoachConversation.Newest);
        Assert.Equal(["/v1/setup", "/v1/tafs/c/chats", "/v1/tafs/c/conversation/new"], asked);
        Assert.Equal("new", newest.Session);

        asked.Clear();
        Assert.Equal(new CoachLoad(null, null, "c", null), await CoachChat.LoadAsync(En, Read, CoachConversation.New));
        Assert.Equal(["/v1/setup"], asked);
    }

    /// <summary>As the iPad's setupTarget: a new conversation asks for one, a chosen one names it, and each follows a hand-over.</summary>
    [Fact]
    public void A_message_goes_to_the_conversation_shown()
    {
        static string Body(CoachConversation to) => Encoding.UTF8.GetString(CoachSend.Body("Hi", to));
        Assert.Equal("""{"text":"Hi","new_chat":true,"new_chat_after_handover":true}""", Body(CoachConversation.New));
        Assert.Equal("""{"text":"Hi","session":"s1","new_chat_after_handover":true}""", Body(CoachConversation.Of("s1")));
        Assert.Equal("""{"text":"Hi","new_chat_after_handover":true}""", Body(CoachConversation.Newest));
        Assert.Equal("/v1/tafs/c%2F1/messages", CoachSend.Path("c/1"));
    }

    [Fact]
    public async Task Sent_names_its_conversation_and_not_taken_says_why()
    {
        // Improving a taf: the first message goes to that taf's own route, with only the words.
        var asked = new List<(string Path, string Body)>();
        Assert.Equal(("s_imp", (Refusal?)null), await CoachSend.ImproveAsync(En, "depl_1", "How is it going?", (path, body) =>
        {
            asked.Add((path, System.Text.Encoding.UTF8.GetString(body)));
            return Task.FromResult(CloudRead.Of(System.Text.Json.JsonDocument.Parse("""{"setup":"taf-setup","session":"s_imp","new_session":true}""").RootElement));
        }));
        Assert.Equal([("/v1/tafs/depl_1/improve", """{"text":"How is it going?"}""")], asked);
        Assert.Equal(("s2", (Refusal?)null), await CoachSend.SendAsync(En, "c", "Hi", CoachConversation.New,
            (_, _) => Task.FromResult(CloudRead.Of(Json("""{"session": "s2", "new_session": true}""")))));
        Assert.Equal((null, new Refusal("Try again in a minute.", Status: 503)), await CoachSend.SendAsync(En, "c", "Hi", CoachConversation.New,
            (_, _) => Task.FromResult(CloudRead.Refused(En, 503, """{"words": "Try again in a minute."}"""))));
        // Refused because the account is halted: the bubble then offers Resume.
        Assert.Equal((null, new Refusal("Your account is halted.", "account", Status: 409)), await CoachSend.SendAsync(En, "c", "Hi", CoachConversation.New,
            (_, _) => Task.FromResult(CloudRead.Refused(En, 409, """{"words": "Your account is halted.", "halted": "account"}"""))));
        Assert.Equal((null, new Refusal(En.Get("error.ours"))), await CoachSend.SendAsync(En, "c", "Hi", CoachConversation.New,
            (_, _) => Task.FromResult(CloudRead.Of(Json("""{"ok": true}""")))));
    }

    /// <summary>As Android's Chat.steady: what's drawn keeps its place, new goes after, gone goes.</summary>
    [Fact]
    public void A_read_again_keeps_the_order_already_drawn()
    {
        static ThreadItem M(string text, int minute) =>
            new(new ChatMessage(new DateTimeOffset(2026, 11, 16, 9, minute, 0, TimeSpan.Zero), ChatFrom.Taf, false, text, "s"), null);
        var (a, b, earlier) = (M("a", 1), M("b", 2), M("earlier", 0));
        Assert.Equal([earlier, a, b], CoachChat.Steady(null, [earlier, a, b]));
        Assert.Equal([a, b, earlier], CoachChat.Steady([a.Id, b.Id], [earlier, a, b]));
        Assert.Equal([b, earlier], CoachChat.Steady([a.Id, b.Id], [earlier, b]));
    }

    /// <summary>The iPad's handle: wider or narrower, full screen near the window's width, closed well past a third.</summary>
    [Fact]
    public void A_drag_of_the_edge_widens_narrows_fills_or_closes()
    {
        var third = CoachWidth.Opening;
        Assert.Equal(400, CoachScreen.PanelWidth(1200, third), 6);
        Assert.Equal(new CoachWidth(0.5, false), CoachScreen.Dragged(1200, third, -200));
        Assert.Equal(new CoachWidth(third.Share, true), CoachScreen.Dragged(1200, third, -770));
        Assert.Equal(third with { Share = CoachScreen.Narrowest }, CoachScreen.Dragged(1200, third, 50));
        Assert.Null(CoachScreen.Dragged(1200, third, 61));
        Assert.Equal(1200, CoachScreen.PanelWidth(1200, new CoachWidth(0.5, true)));
        Assert.Equal(new CoachWidth(0.5, false), CoachScreen.Dragged(1200, new CoachWidth(0.5, true), 600));
        Assert.Equal(350, CoachScreen.Dragging(1200, third, 50), 6);
        Assert.Equal(1200, CoachScreen.Dragging(1200, third, -2000));
        Assert.Equal(0, CoachScreen.Dragging(1200, third, 2000));
    }

    [Theory]
    [InlineData("0.5 0", 0.5, false)]
    [InlineData("0.5 1", 0.5, true)]
    [InlineData("0.2 0", 1.0 / 3, false)]
    [InlineData("1.5 0", 1.0 / 3, false)]
    [InlineData("0,5 0", 1.0 / 3, false)]
    [InlineData("", 1.0 / 3, false)]
    [InlineData(null, 1.0 / 3, false)]
    public void The_chosen_width_is_kept_and_read_back(string? kept, double share, bool full)
    {
        Assert.Equal(new CoachWidth(share, full), CoachWidth.Read(kept));
        Assert.Equal(new CoachWidth(0.42, true), CoachWidth.Read(new CoachWidth(0.42, true).Kept()));
    }
}

using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>
/// Coach's thread, as the iPad's setupThread(decoding:), loadSetupThread, gapTime and WorkLog. tafs cloud has no contract for
/// these reads yet: the answers here are shaped as the iPad's decoders read them, until it writes one.
/// </summary>
[Part("coach")]
public class CoachThreadTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    const string Thread = """
        {"messages": [
          {"at": "2026-11-15T09:00:00Z", "from": "owner", "text": "Set up refunds", "session": "s1", "id": "s1.in"},
          {"at": "2026-11-15T09:00:20Z", "from": "taf", "text": "Done.", "session": "s1",
           "work": {"ms": 12400, "steps": [{"tool": "kiff__planque"}, {"tool": "show"}, {"tool": "kiff_draft", "error": true}, {"tool": "kiff_draft"}]},
           "counted": " From the record: 1 done ", "delivered": "  ", "refused": {"words": "Coach couldn't connect Shippo yet", "next": "Try again"}},
          {"at": "2026-11-15T08:00:00Z", "from": "owner", "text": "Hello", "session": "s0"},
          {"at": "2026-11-15T08:00:00Z", "from": "owner", "text": "Hello", "session": "s0"}],
         "coach_lines": [{"at": "2026-11-15T09:01:00Z", "taf": "Rae", "said": "Refunded #4471", "away": true}, {"taf": "no time"}],
         "working": {"session": "s1"}}
        """;

    [Fact]
    public void The_thread_reads_its_messages_work_and_lines_in_order_each_once()
    {
        var thread = CoachThread.Read(Json(Thread))!;

        Assert.Equal(new[] { "Hello", "Set up refunds", "Done." }, thread.Items.Select(i => i.Message!.Text));
        var answer = thread.Items[2].Message!;
        Assert.Equal((ChatFrom.Taf, 12400, 4), (answer.From, answer.Work!.Ms, answer.Work.Steps.Count));
        Assert.Equal(("From the record: 1 done", (string?)null), (answer.Counted, answer.Delivered));
        Assert.Equal(new[] { "Coach couldn't connect Shippo yet", "Try again" }, answer.Refused);
        Assert.Equal(new CoachLine(new DateTimeOffset(2026, 11, 15, 9, 1, 0, TimeSpan.Zero), "Rae", "Refunded #4471", true), Assert.Single(thread.Lines));
        Assert.Equal("s1", thread.Working);
    }

    /// <summary>As the iPad's decoder: a message missing what it requires fails the thread; work it can't read is left off.</summary>
    [Fact]
    public void A_message_it_can_t_read_fails_the_thread_and_work_it_can_t_read_is_left_off()
    {
        Assert.Null(CoachThread.Read(Json("""{"messages": [{"at": "2026-11-15T09:00:00Z", "from": "owner", "text": "x"}]}""")));
        Assert.Null(CoachThread.Read(Json("""{"messages": [{"at": "2026-11-15T09:00:00Z", "from": "bot", "text": "x", "session": "s"}]}""")));
        Assert.Null(CoachThread.Read(Json("""{"coach_lines": []}""")));
        var m = CoachThread.Read(Json("""{"messages": [{"at": "2026-11-15T09:00:00Z", "from": "taf", "text": "x", "session": "s", "work": {"ms": 1, "steps": [{"error": true}]}}]}"""))!;
        Assert.Null(m.Messages[0].Work);
    }

    /// <summary>The iPad's loadSetupThread at launch: Coach's id, its newest conversation by when it was updated, then that one.</summary>
    [Fact]
    public async Task It_reads_Coach_then_its_newest_conversation()
    {
        var asked = new List<string>();
        Task<CloudRead> Read(string path)
        {
            asked.Add(path);
            return Task.FromResult(CloudRead.Of(Json(path switch
            {
                "/v1/setup" => """{"setup": {"id": "taf-coach/1"}}""",
                "/v1/tafs/taf-coach%2F1/chats" => """{"chats": [{"session": "old", "title": "a", "started": "2026-11-14T08:00:00Z", "updated": "2026-11-14T09:00:00Z"}, {"session": "new", "title": "b", "started": "2026-11-15T08:00:00Z", "updated": "2026-11-15T09:00:00Z"}]}""",
                _ => Thread,
            })));
        }

        var (thread, error, coach) = await CoachChat.LoadAsync(En, Read);

        Assert.Equal(new[] { "/v1/setup", "/v1/tafs/taf-coach%2F1/chats", "/v1/tafs/taf-coach%2F1/conversation/new" }, asked);
        Assert.Equal(4, thread!.Messages.Count);
        Assert.Null(error);
        Assert.Equal("taf-coach/1", coach);
    }

    /// <summary>As the iPad: no Coach, or /v1/setup unread, is no thread and no error (try?); a conversation unread says why.</summary>
    [Fact]
    public async Task With_no_Coach_there_s_no_thread_and_a_conversation_unread_says_why()
    {
        Assert.Equal((null, null, null), await CoachChat.LoadAsync(En, _ => Task.FromResult(CloudRead.Of(Json("""{"setup": null}""")))));
        Assert.Equal((null, null, null), await CoachChat.LoadAsync(En, _ => Task.FromResult(CloudRead.Unreached(En))));

        Task<CloudRead> Unread(string path) => Task.FromResult(path == "/v1/setup" ? CloudRead.Of(Json("""{"setup": {"id": "c"}}"""))
            : path.EndsWith("/chats") ? CloudRead.Unreached(En) : CloudRead.Refused(En, 503, """{"words": "Try again in a minute."}"""));
        var (_, error, _) = await CoachChat.LoadAsync(En, Unread);
        Assert.Equal("The conversation couldn't be loaded: Try again in a minute.", error);

        Task<CloudRead> Undecodable(string path) => Task.FromResult(CloudRead.Of(Json(path == "/v1/setup" ? """{"setup": {"id": "c"}}""" : """{"messages": [{"text": "no time"}]}""")));
        Assert.Equal(En.Get("coach.conversation_not_loaded", ("why", En.Get("error.ours"))), (await CoachChat.LoadAsync(En, Undecodable)).Error);
    }

    /// <summary>
    /// As the iPad's strict [Chat] and chats.max(by:): a list with one entry it can't read fails, and one with none yet has no
    /// newest; either way the whole thread is read, never an error.
    /// </summary>
    [Theory]
    [InlineData("""{"chats": [{"session": "new", "title": "b", "started": "2026-11-15T08:00:00Z", "updated": "2026-11-15T09:00:00Z"}, {"session": "x", "updated": "2026-11-15T10:00:00Z"}]}""")]
    [InlineData("""{"chats": []}""")]
    public async Task A_list_it_can_t_read_or_an_empty_one_reads_the_whole_thread(string chats)
    {
        var asked = new List<string>();
        Task<CloudRead> Read(string path)
        {
            asked.Add(path);
            return Task.FromResult(CloudRead.Of(Json(path switch { "/v1/setup" => """{"setup": {"id": "c"}}""", "/v1/tafs/c/chats" => chats, _ => Thread })));
        }

        var (thread, error, _) = await CoachChat.LoadAsync(En, Read);

        Assert.Equal("/v1/tafs/c/conversation", asked[^1]);
        Assert.Equal((true, (string?)null), (thread is not null, error));
    }

    /// <summary>As the iPad's ownerWords: a card's answer as what was chosen (a key), anything else as written.</summary>
    [Theory]
    [InlineData("[approval a1] declined", "coach.answer.decline", null)]
    [InlineData("[approval a1] approved", "approval.approve", null)]
    [InlineData("[approval a1] failed: no key", "coach.answer.approve_failed", null)]
    [InlineData("[sign_in s1] not now", "common.not_now", null)]
    [InlineData("[drive d1] declined", "common.not_now", null)]
    [InlineData("[confirm c1] refuse", null, "refuse")]
    [InlineData("[nothing c1] yes", null, "[nothing c1] yes")]
    [InlineData("Just words", null, "Just words")]
    public void An_answer_to_a_card_reads_as_what_the_owner_chose(string text, string? key, string? written) =>
        Assert.Equal(key is null ? written : En.Get(key), CoachChat.OwnerWords(En, text));

    /// <summary>The thread's days: a header over its first message, none again the same day (<see cref="ChatTime"/>).</summary>
    [Fact]
    public void The_thread_s_first_message_starts_its_day()
    {
        var items = CoachThread.Read(Json(Thread))!.Items;
        Assert.True(ChatTime.StartsDay(items, 0, TimeZoneInfo.Utc));
        Assert.All(Enumerable.Range(1, items.Count - 1), i =>
            Assert.Equal(items[i].At.UtcDateTime.Date != items[i - 1].At.UtcDateTime.Date, ChatTime.StartsDay(items, i, TimeZoneInfo.Utc)));
    }

    /// <summary>Coach's work in its own words, what it showed left out, a failed try folded into the try that worked.</summary>
    [Fact]
    public void Coach_s_work_folds_into_rows_in_its_own_words()
    {
        var work = CoachThread.Read(Json(Thread))!.Items[2].Message!.Work!;

        var rows = WorkLog.CoachRows(En, work);

        Assert.Equal(new[] { new WorkRow(En.Get("coach.activity.connection"), StepStatus.Done, 1), new WorkRow(En.Get("coach.activity.card"), StepStatus.Done, 2) }, rows);
        Assert.Equal("12 s", WorkLog.Duration(En, work.Ms));
        Assert.Equal("1 s", WorkLog.Duration(En, 10));
        Assert.Equal(En.Get("work.duration.minutes", ("minutes", "1"), ("seconds", "5")), WorkLog.Duration(En, 65_000));
    }

    /// <summary>When nothing worked, the failures stay, to say why; while at work, they're left out.</summary>
    [Fact]
    public void Failures_stay_only_when_nothing_worked()
    {
        var failed = new[] { ("Reading", StepStatus.Error), ("Writing", StepStatus.Error) };
        Assert.Equal(2, WorkLog.Rows(failed, running: false).Count);
        Assert.Empty(WorkLog.Rows(failed, running: true));
        Assert.Equal("Reading", WorkLog.Past("Reading…"));
    }
}

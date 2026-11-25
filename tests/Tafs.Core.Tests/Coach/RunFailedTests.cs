using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Coach;
using Tafs.Core.Contracts;
using Tafs.Core.Tests.Strings;

namespace Tafs.Core.Tests.Coach;

/// <summary>
/// A run that ended without an answer, and Try again under its line only when tafs cloud says it's safe (<c>failed.retry</c>),
/// read from contracts/conversation-failed.json at the pin, as Android's RunFailedTest.
/// </summary>
[Part("coach")]
public class RunFailedTests
{
    static JsonObject Contract() => (JsonObject)JsonNode.Parse(JsonSerializer.Serialize(ContractFile.Answer(SharedFiles.Contract("conversation-failed.json"))))!;

    /// <summary>The contract's conversation, with its <c>failed</c> changed by <paramref name="edit"/>.</summary>
    static CoachThread With(Action<JsonObject>? edit = null)
    {
        var answer = Contract();
        edit?.Invoke((JsonObject)answer["failed"]!);
        return CoachThread.Read(JsonDocument.Parse(answer.ToJsonString()).RootElement)!;
    }

    static FailedRetry? Again(CoachThread t, string? retried = null, bool busy = false) => FailedRetry.Of(t.Failed, t.Messages, retried, busy);

    [Fact]
    public void The_contract_s_retry_is_Try_again_in_tafs_cloud_s_words_with_the_run_s_own_message()
    {
        var sent = Contract()["failed"]!;
        Assert.True(sent["retry"]!.GetValue<bool>());
        var t = With();
        Assert.Equal(sent["words"]!.GetValue<string>(), t.Failed!.Words);
        Assert.Equal(sent["code"]!.GetValue<string>(), t.Failed.Code);
        var again = Again(t)!;
        Assert.Equal(sent["run"]!.GetValue<string>(), again.Run);
        Assert.Equal(sent["retry_label"]!.GetValue<string>(), again.Label);
        // The person's own message for that run (`<run>.in`), as the contract has it.
        Assert.Equal("Connect my Stripe", again.Text);
    }

    [Fact]
    public void No_retry_from_tafs_cloud_is_no_Try_again_and_the_line_stays()
    {
        var off = With(f => { f["retry"] = false; f.Remove("retry_label"); });
        Assert.Null(Again(off));
        Assert.NotNull(off.Failed);
        Assert.Null(off.Failed!.RetryLabel);
        var missing = With(f => { f.Remove("retry"); f.Remove("retry_label"); });
        Assert.Null(Again(missing));
        Assert.Equal(Contract()["failed"]!["words"]!.GetValue<string>(), missing.Failed!.Words);
    }

    [Fact]
    public void A_code_tafs_cloud_isn_t_known_to_send_is_words_only()
    {
        Assert.Null(Again(With(f => f["code"] = "something_new")));
        Assert.Null(Again(With(f => f.Remove("code"))));
        foreach (var code in new[] { "ai_unavailable", "no_credits", "ai_consent_required", "timeout", "failed" })
            Assert.NotNull(Again(With(f => f["code"] = code)));
    }

    [Fact]
    public void Without_the_run_s_own_message_there_is_nothing_to_send_again() =>
        Assert.Null(Again(With(f => f["run"] = "run7")));

    [Fact]
    public void No_label_from_tafs_cloud_leaves_the_app_s_own_word()
    {
        Assert.Null(Again(With(f => f.Remove("retry_label")))!.Label);
        Assert.Null(Again(With(f => f["retry_label"] = "  "))!.Label);
    }

    [Fact]
    public void Pressed_once_or_while_the_chat_is_busy_there_is_no_Try_again()
    {
        var t = With();
        Assert.Null(Again(t, retried: t.Failed!.Run));
        Assert.NotNull(Again(t, retried: "another run"));
        Assert.Null(Again(t, busy: true));
    }

    [Fact]
    public void A_message_with_files_or_a_card_s_answer_is_words_only()
    {
        var t = With();
        var own = t.Messages.Single(m => m.ServerId == "run2.in");
        var withFiles = t with { Messages = [own with { Files = [new ChatFile("f1", "file", "text/plain", "a.txt", 3)] }] };
        Assert.Null(Again(withFiles));
        var answering = t with { Messages = [own with { Answers = "which" }] };
        Assert.Null(Again(answering));
    }

    [Fact]
    public void A_failed_without_its_run_or_words_is_no_line()
    {
        Assert.Null(With(f => f.Remove("run")).Failed);
        Assert.Null(With(f => f["words"] = " ").Failed);
        var none = Contract();
        none["failed"] = null;
        Assert.Null(CoachThread.Read(JsonDocument.Parse(none.ToJsonString()).RootElement)!.Failed);
    }
}

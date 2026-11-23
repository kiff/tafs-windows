using Tafs.Core.NeedsYou;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.NeedsYou;

/// <summary>The paused run asked to go on after a held call's answer: a refusal is said, never swallowed.</summary>
[Part("approvals")]
public class HeldContinueTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    [Fact]
    public void The_path_is_tafs_cloud_s_continue_for_the_taf_s_hold()
    {
        Assert.Equal("/v1/tafs/taf_1/holds/ce_9/continue", HeldContinue.Path("taf_1", "ce_9"));
        Assert.Equal("/v1/tafs/a%2Fb/holds/c%20d/continue", HeldContinue.Path("a/b", "c d"));
    }

    [Fact]
    public void Taken_says_nothing_and_a_refusal_says_tafs_cloud_s_words_else_what_didn_t_go_through()
    {
        Assert.Null(HeldContinue.Said(En, "Refund €150", CloudRead.Of(System.Text.Json.JsonDocument.Parse("""{"status":"running"}""").RootElement)));
        // The run had ended: tafs cloud's words, in the owner's language (kiff/tafs#1812).
        var ended = CloudRead.Refused(En, 404, """{"error":"run ended","code":"run_ended","words":" That run had already ended, so nothing ran. "}""");
        Assert.Equal("That run had already ended, so nothing ran.", HeldContinue.Said(En, "Refund €150", ended));
        // KIFF's alert continued it first (409 not_paused): it went on, so nothing is said, even with words.
        Assert.Null(HeldContinue.Said(En, "Refund €150", CloudRead.Refused(En, 409, """{"error":"not paused","code":"not_paused","words":"x"}""")));
        Assert.Null(HeldContinue.Said(En, "Refund €150", CloudRead.Refused(En, 409, "")));
        // Any other refusal, or one with no words: what didn't go through, and why.
        Assert.Equal("Refund €150 didn't go through. It didn't reach the server. Try again once you're online.",
            HeldContinue.Said(En, "Refund €150", CloudRead.Unreached(En)));
        Assert.Equal("Refund €150 didn't go through. nope", HeldContinue.Said(En, "Refund €150", CloudRead.Refused(En, 500, """{"error":"nope"}""")));
    }
}

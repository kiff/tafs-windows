using Tafs.Core.Approvals;
using Tafs.Core.NeedsYou;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Approvals;

/// <summary>What stands in an approval's place while Windows Hello can't confirm, as the iPad's ApprovalGate and ApprovalReadiness.short.</summary>
[Part("approvals")]
public class HelloGateTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    public static IEnumerable<object[]> Shipped() => WordBook.Shipped(SharedFiles.Strings).Select(l => new object[] { l });

    [Fact]
    public void Ready_is_the_button_and_each_other_state_says_why_and_what_to_do()
    {
        Assert.Null(HelloGate.NotReady(En, HelloReadiness.Ready));

        var notSetUp = HelloGate.NotReady(En, HelloReadiness.NotSetUp)!;
        Assert.True(notSetUp.OpensSettings);
        Assert.True(notSetUp.ChecksAgain);
        Assert.Contains("Windows Hello", notSetUp.Line);

        Assert.Equal(new HelloNotReady("Windows Hello can't be used right now. Try again in a moment.", false, true),
            HelloGate.NotReady(En, HelloReadiness.Busy));
        Assert.Equal(new HelloNotReady("This device can't approve. Approve on another device signed in to your account.", false, false),
            HelloGate.NotReady(En, HelloReadiness.Unsupported));
    }

    [Theory]
    [MemberData(nameof(Shipped))]
    public void Every_language_says_each_state(string language)
    {
        var w = WordBook.Load(SharedFiles.Strings, language);
        foreach (var state in new[] { HelloReadiness.NotSetUp, HelloReadiness.Busy, HelloReadiness.Unsupported })
        {
            var line = HelloGate.NotReady(w, state)!.Line;
            Assert.False(string.IsNullOrWhiteSpace(line));
            Assert.DoesNotContain("{", line);
        }
    }
}

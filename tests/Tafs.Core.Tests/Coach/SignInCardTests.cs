using System.Text;
using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>Coach's sign-in card, as the iPad's SetupChatView.signIn and setupCardState for a sign-in.</summary>
[Part("coach")]
public class SignInCardTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static CoachThread Thread(string more = "", string answer = "") => CoachThread.Read(JsonDocument.Parse($$$"""
        {"messages": [{"at": "2026-11-15T09:00:00Z", "from": "owner", "text": "Connect Gmail", "session": "s1"}],
         "components": [{"at": "2026-11-15T09:00:05Z", "session": "s1", "run": "r1", "component": {"type": "sign_in", "id": "s1", "grant": "g_01"{{{more}}}}{{{answer}}}}]}
        """).RootElement)!;

    static PlanqueGrant Grant(string status, string more = "") =>
        PlanqueGrants.Read(Encoding.UTF8.GetBytes($$"""{"grants": [{"id": "g_01", "status": "{{status}}", "text": "t", "digest": "d"{{more}}, "request": {"server": "https://gmail.mcp", "agent": "rae", "tools": []}, "vendor": "Gmail", "events": []}]}"""))![0];

    static CoachCard Card(CoachThread t) => t.Shown[0].Card;

    [Fact]
    public void It_stands_as_the_iPad_s_sign_in_does()
    {
        var waiting = Thread();
        Assert.Equal(ApprovalState.NeedsYou, SignInCard.State(waiting, Card(waiting), Grant("approved")).State);
        // planque's grant connected, or holding the sign-in (listed): done, with no answer.
        Assert.Equal(ApprovalState.Done, SignInCard.State(waiting, Card(waiting), Grant("connected")).State);
        Assert.Equal(ApprovalState.Done, SignInCard.State(waiting, Card(waiting), Grant("listed")).State);
        Assert.Equal((ApprovalState.Failed, "expired"), SignInCard.State(waiting, Card(waiting), Grant("failed", """, "reason": "expired" """)));

        var signedIn = Thread(answer: """, "answer": {"state": "answered", "words": "Signed in"}""");
        Assert.Equal(ApprovalState.Done, SignInCard.State(signedIn, Card(signedIn), null).State);
        var notNow = Thread(answer: """, "answer": {"state": "answered", "words": "Not now"}""");
        Assert.Equal(ApprovalState.Declined, SignInCard.State(notNow, Card(notNow), null).State);
    }

    /// <summary>The app by name: the card's service, else planque's; "the vendor" only with neither. Where it goes is never Coach's words.</summary>
    [Fact]
    public void It_names_the_app_and_where_it_goes()
    {
        var withService = Thread(""", "service": "Gmail", "yes": "Sign in with Google" """);
        Assert.Equal("Sign in at Gmail", SignInCard.Title(En, Card(withService), null));
        Assert.Equal("Sign in with Google", SignInCard.Button(En, Card(withService), null, failed: false));
        Assert.Equal("Opens Gmail", SignInCard.Opens(En, Card(withService)));

        var bare = Thread();
        Assert.Equal("Sign in at Gmail", SignInCard.Title(En, Card(bare), Grant("approved")));
        Assert.Equal("Sign in at the vendor", SignInCard.Title(En, Card(bare), null));
        Assert.Equal("Open the sign-in", SignInCard.Button(En, Card(bare), null, failed: false));
        Assert.Equal("Opens the vendor's page", SignInCard.Opens(En, Card(bare)));
        Assert.Equal("Try again", SignInCard.Button(En, Card(bare), null, failed: true));
        Assert.Equal("Signed in at Gmail", SignInCard.Outcome(En, Card(bare), Grant("connected"), ApprovalState.Done));
        Assert.Equal("Not now: no sign-in at the vendor", SignInCard.Outcome(En, Card(bare), null, ApprovalState.Declined));
    }
}

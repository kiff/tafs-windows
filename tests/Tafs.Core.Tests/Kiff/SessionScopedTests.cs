using Tafs.Core.Kiff;

namespace Tafs.Core.Tests.Kiff;

/// <summary>A session's in-memory caches (a taf's Setup): never the last account's for the next.</summary>
[Part("pairing")]
public class SessionScopedTests
{
    [Fact]
    public void A_value_is_kept_for_its_session_only()
    {
        var kept = new SessionScoped<string, List<string>>();
        object first = new(), next = new();
        kept.Get(first, "t1", () => []).Add("Stripe");
        Assert.Equal(["Stripe"], kept.Get(first, "t1", () => []));
        // Another session: the last one's values never answer.
        Assert.Empty(kept.Get(next, "t1", () => []));
        Assert.Equal(1, kept.Count);
    }

    [Fact]
    public void Signing_out_forgets_everything()
    {
        var kept = new SessionScoped<string, List<string>>();
        object session = new();
        kept.Get(session, "t1", () => []).Add("Stripe");
        kept.Forget();
        Assert.Equal(0, kept.Count);
        // Even asked again by the same session's object, nothing of before comes back.
        Assert.Empty(kept.Get(session, "t1", () => []));
    }
}

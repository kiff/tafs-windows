using Tafs.Core.Kiff;

namespace Tafs.Core.Tests.Kiff;

[Part("pairing")]
public class KeptSessionTests
{
    static readonly Uri Api = new("https://api.kiff.dev");
    static readonly DateTimeOffset Now = new(2026, 11, 15, 3, 0, 0, TimeSpan.Zero);

    /// <summary>The store as bytes on disk: a new instance over the same bytes is a relaunch.</summary>
    sealed class FakeStore(byte[]? bytes = null) : ISessionStore
    {
        public byte[]? Bytes = bytes;
        public bool Refuses;
        public void CheckWritable() { if (Refuses) throw new IOException("refused"); }
        public void Save(byte[] data) { if (Refuses) throw new IOException("refused"); Bytes = data; }
        public byte[]? Load() => Bytes;
        public void Delete() => Bytes = null;
    }

    static KeptSession Session(string token, ApprovalMode mode, string key, string host = "api.kiff.dev") =>
        KeptSession.Of(new Uri("https://" + host), token, "t1", "s1", "dev_1", mode, TimeSpan.FromDays(90), Now, key);

    [Theory]
    [InlineData("view", ApprovalMode.View)]
    [InlineData("key", ApprovalMode.Key)]
    [InlineData("passkey", ApprovalMode.Passkey)]
    [InlineData("device_key", ApprovalMode.View)]
    [InlineData("anything", ApprovalMode.View)]
    [InlineData(null, ApprovalMode.View)]
    public void An_unknown_approval_mode_counts_as_view(string? word, ApprovalMode mode) => Assert.Equal(mode, KeptSession.ModeOf(word));

    [Fact]
    public void A_kept_session_is_read_again_after_a_relaunch()
    {
        var disk = new FakeStore();
        var signedIn = Session("view-token", ApprovalMode.View, "tafs device key");
        Assert.Null(KeptSessions.Replace(disk, Api, signedIn, Now));

        var relaunched = new FakeStore(disk.Bytes);
        Assert.Equal(signedIn, KeptSessions.Read(relaunched.Load(), Api, Now.AddDays(1)));
    }

    [Fact]
    public void A_new_device_s_session_replaces_the_kept_one_and_signs_it_out()
    {
        var disk = new FakeStore();
        var view = Session("view-token", ApprovalMode.View, "tafs device key");
        KeptSessions.Replace(disk, Api, view, Now);

        var old = KeptSessions.Replace(disk, Api, Session("paired-token", ApprovalMode.Passkey, "tafs paired device key"), Now);
        Assert.Equal(view, old); // to sign out at KIFF, and its key to remove
        Assert.Equal("paired-token", KeptSessions.Read(disk.Load(), Api, Now)!.AccessToken);
        // The same device's session again (same key) isn't signed out.
        Assert.Null(KeptSessions.Replace(disk, Api, Session("new-token", ApprovalMode.Passkey, "tafs paired device key"), Now));
    }

    [Fact]
    public void A_store_that_refuses_keeps_nothing_and_says_so()
    {
        var disk = new FakeStore { Refuses = true };
        Assert.Throws<IOException>(() => disk.CheckWritable());
        Assert.Throws<IOException>(() => KeptSessions.Replace(disk, Api, Session("x", ApprovalMode.Passkey, "k"), Now));
        Assert.Null(disk.Bytes);
    }

    [Fact]
    public void A_session_is_kept_only_for_KIFF_s_exact_host_and_until_it_expires()
    {
        var other = KeptSessions.Write(Session("x", ApprovalMode.Passkey, "k", host: "kiff.dev.example.com"));
        Assert.Null(KeptSessions.Read(other, Api, Now));
        Assert.Null(KeptSessions.Read(KeptSessions.Write(Session("x", ApprovalMode.Passkey, "k", host: "app.kiff.dev")), Api, Now));
        var ours = KeptSessions.Write(Session("x", ApprovalMode.Passkey, "k"));
        Assert.NotNull(KeptSessions.Read(ours, Api, Now.AddDays(89)));
        Assert.Null(KeptSessions.Read(ours, Api, Now.AddDays(90)));
        Assert.Null(KeptSessions.Read("not json"u8.ToArray(), Api, Now));
        Assert.Null(KeptSessions.Read("""{"host":"api.kiff.dev"}"""u8.ToArray(), Api, Now));
        Assert.Null(KeptSessions.Read(null, Api, Now));
    }

    [Fact]
    public void The_token_is_never_printed() =>
        Assert.DoesNotContain("secret-token", Session("secret-token", ApprovalMode.View, "k").ToString());
}

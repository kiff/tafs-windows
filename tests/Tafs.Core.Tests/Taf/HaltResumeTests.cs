using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Kiff;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>Resuming a halt, as the iPad's resumeHalt: signed by this PC's passkey, KIFF first, then tafs cloud.</summary>
[Part("record")]
public class HaltResumeTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static KeptSession Session(ApprovalMode mode, string? passkey = null) =>
        new KeptSession("api.kiff.dev", "token", "t_01", "s1", "dev_pc01", mode, DateTimeOffset.MaxValue, "k") { PasskeyId = passkey };

    const string Resumed = """{"halt":{"state":"running","runtime":false,"kiff":false,"planque":false,"account":false}}""";

    /// <summary>Every call, in order, with what it answers.</summary>
    sealed class Calls
    {
        public readonly List<string> Made = [];
        public Exception? SignThrows;
        public int KiffStatus = 200;
        public CloudRead Release = CloudRead.Of(JsonDocument.Parse(Resumed).RootElement.Clone());
        public bool Current = true;
        public bool SignOutAfterSign;

        public Task<(string, string)> Sign(string path)
        {
            Made.Add("sign " + path);
            if (SignThrows is { } e) return Task.FromException<(string, string)>(e);
            if (SignOutAfterSign) Current = false;
            return Task.FromResult(("KIFF-Passkey-Assertion", "t=x,a=y"));
        }

        public Task<int> Kiff(string path, (string Header, string Value) s)
        {
            Made.Add($"kiff {path} {s.Header}");
            return Task.FromResult(KiffStatus);
        }

        public Task<CloudRead> Cloud(string path)
        {
            Made.Add("cloud " + path);
            return Task.FromResult(Release);
        }

        public Task<(Halt? Halt, string? Why)> Resume(string? taf, string? agent, string? name) =>
            HaltResume.ResumeAsync(En, taf, agent, name, Sign, Kiff, Cloud, () => Current);
    }

    /// <summary>One taf at KIFF by its agent, every taf at /v1/resume; tafs cloud releases its own halt after KIFF did.</summary>
    [Fact]
    public async Task Resume_signs_then_tells_KIFF_then_tafs_cloud()
    {
        var one = new Calls();
        var (halt, why) = await one.Resume("taf_1", "rae", "Rae");
        Assert.Null(why);
        Assert.Equal(HaltState.Running, halt!.State);
        Assert.Equal(["sign /v1/agents/rae/resume", "kiff /v1/agents/rae/resume KIFF-Passkey-Assertion", "cloud /v1/tafs/taf_1/halt"], one.Made);

        var all = new Calls();
        Assert.Null((await all.Resume(null, null, null)).Why);
        Assert.Equal(["sign /v1/resume", "kiff /v1/resume KIFF-Passkey-Assertion", "cloud /v1/halt"], all.Made);
    }

    /// <summary>Windows Hello closed, or failing, sends nothing; each says so in the app's words.</summary>
    [Fact]
    public async Task A_resume_not_confirmed_sends_nothing()
    {
        var cancelled = new Calls { SignThrows = new OperationCanceledException() };
        Assert.Equal((null, En.Get("approval.cancelled_nothing_approved_request_kept")), await cancelled.Resume("taf_1", "rae", "Rae"));
        Assert.Equal(["sign /v1/agents/rae/resume"], cancelled.Made);

        foreach (var e in new Exception[] { new PasskeyRefusedException("off"), new CryptographicException("tpm"),
            new System.Runtime.InteropServices.COMException("hello"), new InvalidDataException("short") })
        {
            var failed = new Calls { SignThrows = e };
            Assert.Equal((null, En.Get("taf.halt.not_resumed_unconfirmed")), await failed.Resume(null, null, null));
            Assert.Single(failed.Made);
        }
        // A taf with no KIFF agent asks nothing.
        var noAgent = new Calls();
        Assert.NotNull((await noAgent.Resume("taf_1", null, "Rae")).Why);
        Assert.Empty(noAgent.Made);
    }

    /// <summary>KIFF not taking it says so in plain words, and tafs cloud isn't asked; tafs cloud not taking it says KIFF did.</summary>
    [Fact]
    public async Task Each_refusal_says_which_part_didn_t_resume()
    {
        var kiff = new Calls { KiffStatus = 403 };
        Assert.Equal((null, $"KIFF didn't resume Rae. {En.Get("error.not_allowed")}"), await kiff.Resume("taf_1", "rae", "Rae"));
        Assert.DoesNotContain(kiff.Made, c => c.StartsWith("cloud", StringComparison.Ordinal));

        var cloud = new Calls { Release = new CloudRead(null, "tafs cloud is down.", 503) };
        Assert.Equal((null, "KIFF resumed your tafs, but tafs cloud didn't yet. tafs cloud is down."), await cloud.Resume(null, null, null));
    }

    /// <summary>A sign-out after Windows Hello sends nothing more, and says nothing.</summary>
    [Fact]
    public async Task A_sign_out_meanwhile_sends_nothing_more()
    {
        var calls = new Calls { SignOutAfterSign = true };
        Assert.Equal((null, null), await calls.Resume("taf_1", "rae", "Rae"));
        Assert.Equal(["sign /v1/agents/rae/resume"], calls.Made);
    }

    /// <summary>
    /// Only a passkey session that knows its passkey resumes, and its assertion is over exactly this request; any other
    /// session throws before anything is asked.
    /// </summary>
    [Fact]
    public async Task Only_this_PC_s_passkey_signs_the_resume_over_its_exact_request()
    {
        Assert.True(HaltResume.MayResume(Session(ApprovalMode.Passkey, "AQI")));
        Assert.False(HaltResume.MayResume(Session(ApprovalMode.Passkey)));
        Assert.False(HaltResume.MayResume(Session(ApprovalMode.Key, "AQI")));
        Assert.False(HaltResume.MayResume(Session(ApprovalMode.View)));
        Assert.False(HaltResume.MayResume(null));

        using var key = new TestSigner();
        var hello = new Hello(key);
        var passkey = new PasskeyApprover(hello, [1, 2], true);
        var at = "2026-11-16T12:00:00.123Z";
        var (header, value) = await HaltResume.SignAsync(Session(ApprovalMode.Passkey, "AQI"), passkey, "/v1/agents/rae/resume", at);
        Assert.Equal("KIFF-Passkey-Assertion", header);
        Assert.StartsWith($"t={at},a=", value);
        using (var client = JsonDocument.Parse(hello.ClientData!))
            Assert.Equal(PasskeyApprover.Challenge(KiffSigning.RequestV1("t_01", "POST", "/v1/agents/rae/resume", HaltResume.Body(), at)),
                client.RootElement.GetProperty("challenge").GetString());

        await Assert.ThrowsAsync<PasskeyRefusedException>(() => HaltResume.SignAsync(Session(ApprovalMode.Key, "AQI"), passkey, "/v1/resume", at));
        await Assert.ThrowsAsync<PasskeyRefusedException>(() => HaltResume.SignAsync(Session(ApprovalMode.Passkey, "AQI"), null, "/v1/resume", at));
        Assert.Equal("/v1/agents/a%2Fb/resume", HaltResume.KiffPath("a/b"));
    }

    sealed class Hello(TestSigner key) : IPasskeyAuthenticator
    {
        public byte[]? ClientData;

        public async Task<PasskeyAssertion> GetAssertionAsync(string rpId, byte[] credentialId, byte[] clientDataJson, CancellationToken cancel)
        {
            ClientData = clientDataJson;
            var authData = SHA256.HashData(Encoding.UTF8.GetBytes(rpId)).Concat(new byte[] { 0x05, 0, 0, 0, 1 }).ToArray();
            return new PasskeyAssertion(authData, await key.SignDerAsync(authData.Concat(SHA256.HashData(clientDataJson)).ToArray(), cancel));
        }
    }
}

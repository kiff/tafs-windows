using System.Text;
using System.Text.Json;
using Tafs.Core.Kiff;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Kiff;

/// <summary>planque's signed read of the grants, sent with a taf's Setup, as the iPad's grantsReadProof.</summary>
[Part("pairing")]
public class PlanqueProofTests
{
    const string At = "2026-11-15T10:41:31.123Z";

    /// <summary>planque's device.Message: seven lines, no trailing newline, the empty body's SHA-256.</summary>
    [Fact]
    public void The_message_is_planque_s_seven_lines()
    {
        var message = PlanqueProof.Message(PlanqueProof.Api, "ten_1", "GET", PlanqueProof.GrantsPath("ten_1"), [], At);

        Assert.Equal(
            "planque.request.v1\nhttps://planque.kiff.dev\nten_1\nGET\n/v1/accounts/ten_1/grants\n"
            + "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\n" + At,
            Encoding.UTF8.GetString(message));
    }

    [Fact]
    public async Task The_proof_is_the_time_the_signature_and_this_device()
    {
        using var key = new TestSigner();
        var loaded = 0;

        // A device already loaded in this window is used as it is, as the iPad's deviceID once set.
        var proof = await PlanqueProof.GrantsReadAsync("ten_1", "dev_loaded", () => { loaded++; return Task.FromResult<string?>("dev_other"); }, key, () => At);

        var parts = proof!.Split(',');
        Assert.Equal(("t=" + At, "d=dev_loaded"), (parts[0], parts[2]));
        var signature = Convert.FromBase64String(parts[1]["v1=".Length..]);
        Assert.True(TestSigner.Verify(key.PublicKeyDer,
            PlanqueProof.Message(PlanqueProof.Api, "ten_1", "GET", "/v1/accounts/ten_1/grants", [], At), signature));
        Assert.Equal(0, loaded);
    }

    /// <summary>With no device loaded yet, it's loaded from KIFF first; with none there, or no account, there's no proof.</summary>
    [Fact]
    public async Task Without_a_device_it_s_loaded_and_without_one_or_an_account_there_s_none()
    {
        using var key = new TestSigner();
        Assert.EndsWith(",d=dev_9", await PlanqueProof.GrantsReadAsync("ten_1", null, () => Task.FromResult<string?>("dev_9"), key, () => At));
        Assert.Null(await PlanqueProof.GrantsReadAsync("ten_1", null, () => Task.FromResult<string?>(null), key, () => At));
        Assert.Null(await PlanqueProof.GrantsReadAsync(null, "dev_1", () => Task.FromResult<string?>(null), key, () => At));
    }

    /// <summary>As the iPad's thisDeviceID: the one listed as this device, and only a device's id ("dev_…").</summary>
    [Theory]
    [InlineData("""{"devices": [{"id": "dev_a"}, {"id": "dev_b", "this_device": true}]}""", "dev_b")]
    [InlineData("""{"devices": [{"id": "ses_b", "this_device": true}]}""", null)]
    [InlineData("""{"devices": []}""", null)]
    [InlineData("""[]""", null)]
    public void This_device_is_the_one_KIFF_lists_as_this_one(string json, string? id) =>
        Assert.Equal(id, PlanqueProof.ThisDevice(JsonDocument.Parse(json).RootElement));

    /// <summary>As the iPad's SigningTime: milliseconds, and never the same twice, even in the same millisecond.</summary>
    [Fact]
    public void The_signing_time_has_milliseconds_and_never_repeats()
    {
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, 678, TimeSpan.Zero);
        var first = SigningTime.Next(now);
        var second = SigningTime.Next(now);

        Assert.Equal("2030-01-02T03:04:05.678Z", first);
        Assert.Equal("2030-01-02T03:04:05.679Z", second);
    }

    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    /// <summary>Setup sends the proof when there's one, and reads without it when there's none or it fails, as the iPad's try?.</summary>
    [Fact]
    public async Task Setup_sends_the_proof_and_reads_without_it_when_it_fails()
    {
        var sent = new List<string?>();
        Task<CloudRead> Read(string path, string? proof)
        {
            sent.Add(proof);
            return Task.FromResult(CloudRead.Refused(En, 503, ""));
        }
        var setup = new SetupRead();

        await setup.LoadAsync("t", Read, () => Task.FromResult<string?>("t=1,v1=s,d=dev_1"));
        await setup.LoadAsync("t", Read, () => throw new System.Security.Cryptography.CryptographicException("no TPM"));
        await setup.LoadAsync("t", Read, () => Task.FromResult<string?>(null));
        await setup.LoadAsync("t", Read);

        // With a proof: the quick read without it, and the signed one; without one, the quick read alone, never twice.
        Assert.Equal(new string?[] { null, "t=1,v1=s,d=dev_1", null, null, null }, sent);
    }

    /// <summary>
    /// Setup doesn't wait for planque: the quick read draws the page while the signed one is on its way, keeping the
    /// connections already shown; the signed answer then replaces it, its connections with it.
    /// </summary>
    [Fact]
    public async Task Setup_draws_from_the_quick_read_while_planque_is_read()
    {
        var signed = new TaskCompletionSource<CloudRead>();
        static CloudRead Page(string job, string? app) => CloudRead.Of(System.Text.Json.JsonDocument.Parse(
            $$$"""{"workflow": {"title": "{{{job}}}"}, "connections": [{{{(app is null ? "" : $$"""{"id": "c1", "name": "{{app}}"}""")}}}]}""").RootElement);
        Task<CloudRead> Read(string path, string? proof) => proof is null ? Task.FromResult(Page("quick", null)) : signed.Task;
        var setup = new SetupRead();
        var drawn = 0;
        var loading = setup.LoadAsync("t", Read, () => Task.FromResult<string?>("t=1,v1=s,d=dev_1"), () => drawn++);

        Assert.Equal(1, drawn);
        Assert.True(setup.ConnectionsPending);
        Assert.Equal("quick", setup.Page!.Workflow.Title);
        Assert.Empty(setup.Page.Connections);

        signed.SetResult(Page("signed", "Stripe"));
        await loading;
        Assert.False(setup.ConnectionsPending);
        Assert.Equal(("signed", "Stripe"), (setup.Page!.Workflow.Title, setup.Page.Connections.Single().Name));
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tafs.Core.Kiff;

namespace Tafs.Core.Tests.Kiff;

/// <summary>The approval window a PC's own passkey opens, and planque's taps inside it (KIFF's desktop client contract §2.3, §3).</summary>
[Part("connections")]
public class ApprovalWindowTests
{
    sealed class Key : IKiffSigner
    {
        readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public byte[]? Signed { get; private set; }
        public byte[] PublicKeyDer => key.ExportSubjectPublicKeyInfo();
        public Task<byte[]> SignDerAsync(byte[] message, CancellationToken cancel = default)
        {
            Signed = message;
            return Task.FromResult(key.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        }
    }

    const string Win = "win_abcdefghijklmnopqrstuvwxyz";

    [Fact]
    public void Its_opening_says_the_window_message_ending_within_five_minutes()
    {
        var now = new DateTimeOffset(2026, 11, 17, 2, 0, 0, TimeSpan.Zero);
        Assert.Equal("""{"message":"kiff.window.v1","expires":"2026-11-17T02:05:00Z","nonce":"n_0123456789abcdef"}""",
            Encoding.UTF8.GetString(ApprovalWindow.OpenBody(now, "n_0123456789abcdef")));
        var nonce = JsonDocument.Parse(ApprovalWindow.OpenBody(now)).RootElement.GetProperty("nonce").GetString()!;
        Assert.InRange(nonce.Length, 16, 128);
        Assert.Equal("/v1/me/window/" + Win + "/close", ApprovalWindow.ClosePath(Win));
    }

    [Fact]
    public void A_window_reads_only_with_its_id_and_is_used_only_while_open_and_a_device_key_s()
    {
        var now = new DateTimeOffset(2026, 11, 17, 2, 0, 0, TimeSpan.Zero);
        var w = ApprovalWindow.Read(JsonDocument.Parse($$$"""{"window": {"id": "{{{Win}}}", "opened": "2026-11-17T02:00:00Z", "expires": "2026-11-17T02:05:00Z", "open": true, "key": "device", "words": "Tap to approve"}}""").RootElement)!;
        Assert.Equal((Win, true, "device", "Tap to approve"), (w.Id, w.Open, w.Key, w.Words));
        Assert.True(ApprovalWindow.Usable(w, now));
        Assert.False(ApprovalWindow.Usable(w, now.AddMinutes(5)));
        Assert.False(ApprovalWindow.Usable(w with { Open = false }, now));
        Assert.False(ApprovalWindow.Usable(w with { Key = "critical" }, now));
        Assert.Null(ApprovalWindow.Read(JsonDocument.Parse("""{"window": null, "words": "No window"}""").RootElement));
        Assert.Null(ApprovalWindow.Read(JsonDocument.Parse("""{"window": {"id": "win_SHORT", "open": true}}""").RootElement));
        Assert.False(ApprovalWindow.IsId("win_abcdefghijklmnopqrstuvwxy1"));
    }

    [Fact]
    public async Task A_tap_in_the_window_signs_planque_s_v2_message_with_the_window_last()
    {
        var key = new Key();
        var body = ApprovalWindow.ApproveBody("sha256:d1", "p1", "w1");
        Assert.Equal("""{"digest":"sha256:d1","kiff_profile":{"profile_sha256":"p1","words_sha256":"w1"}}""", Encoding.UTF8.GetString(body));
        Assert.Equal("""{"digest":"sha256:d1"}""", Encoding.UTF8.GetString(ApprovalWindow.ApproveBody("sha256:d1")));
        var path = ApprovalWindow.ApprovePath("t_1", "grt_9");
        Assert.Equal("/v1/accounts/t_1/grants/grt_9/approve", path);
        var header = await ApprovalWindow.PlanqueTapAsync("t_1", "dev_1", Win, key, "2026-11-17T02:00:00.000Z", "POST", path, body);
        Assert.StartsWith("t=2026-11-17T02:00:00.000Z,v2=", header);
        Assert.EndsWith(",w=" + Win + ",d=dev_1", header);
        Assert.Equal(string.Join('\n', "planque.request.v2", "https://planque.kiff.dev", "t_1", "POST", path,
            Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(), "2026-11-17T02:00:00.000Z", Win), Encoding.UTF8.GetString(key.Signed!));
        Assert.Equal("""{"confirm":"c_42"}""", Encoding.UTF8.GetString(ApprovalWindow.ConfirmBody("c_42")));
    }

    [Fact]
    public void Planque_s_approve_answers_a_sign_in_page_or_a_code()
    {
        var page = ApprovalWindow.Approved(JsonDocument.Parse("""{"status": "approved", "sign_in_url": "https://accounts.google.com/o/oauth2?x=1", "expires_at": "2026-11-17T02:15:00Z"}""").RootElement)!;
        Assert.Equal(("approved", "accounts.google.com"), (page.Status, page.SignIn!.Host));
        var held = ApprovalWindow.Approved(JsonDocument.Parse("""{"status": "signed_in", "confirm": "c_42"}""").RootElement)!;
        Assert.Equal((null, "c_42"), (held.SignIn, held.ConfirmCode));
        Assert.Null(ApprovalWindow.Approved(JsonDocument.Parse("""{"status": "approved", "sign_in_url": "http://evil.example"}""").RootElement)!.SignIn);
        Assert.Null(ApprovalWindow.Approved(JsonDocument.Parse("""{"sign_in_url": "https://x"}""").RootElement));
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Tafs.Core.Kiff;

namespace Tafs.Core.Tests.Kiff;

/// <summary>The picture a paired Chrome kept before a taf's step, as the iPad's ChromeShotView and Linux's chrome_shots.</summary>
[Part("pairing")]
public class ChromeShotsTests
{
    /// <summary>A WebP's header and a few bytes: what the check looks at (RIFF … WEBP).</summary>
    static readonly byte[] Webp = [.. "RIFF"u8, 0x10, 0, 0, 0, .. "WEBP"u8, .. "VP8L"u8, 1, 2, 3, 4];

    static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static string Hex(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    static string Answer(string step, byte[] bytes, string? sha = null, string? takenAt = "2026-11-22T20:00:00Z", string? image = null)
    {
        var o = new JsonObject { ["step"] = step, ["sha256"] = sha ?? Hex(bytes), ["image"] = image ?? B64(bytes) };
        if (takenAt is not null) o["taken_at"] = takenAt;
        return o.ToJsonString();
    }

    [Fact]
    public void The_reads_are_planque_s_routes_with_the_operation_and_the_step_escaped()
    {
        Assert.Equal("/v1/accounts/t/chromes/screenshots?op=prp_1%26x%3D2", ChromeShots.ListPath("t", "prp_1&x=2"));
        Assert.Equal("/v1/accounts/t/chromes/screenshots/s%2F1", ChromeShots.ShotPath("t", "s/1"));
    }

    [Fact]
    public void The_list_reads_and_none_is_no_link()
    {
        var l = ChromeShots.ReadList(200, """{"shots":[{"step":"s1","taken_at":"2026-11-22T20:00:00Z","sha256":"ab"},{"step":" "},{"step":"s2"}]}""");
        Assert.Equal(["s1", "s2"], l.Select(s => s.Step));
        Assert.Empty(ChromeShots.ReadList(200, """{"shots":[]}"""));
        Assert.Empty(ChromeShots.ReadList(404, ""));
        Assert.Empty(ChromeShots.ReadList(500, ""));
        Assert.Empty(ChromeShots.ReadList(200, "not json"));
    }

    [Fact]
    public void A_picture_is_shown_only_when_it_checks_out()
    {
        var (read, shot) = ChromeShots.Read(200, Answer("s1", Webp), "s1");
        Assert.Equal(ShotRead.Shown, read);
        Assert.Equal(Webp, shot!.Image);
        Assert.Equal(DateTimeOffset.Parse("2026-11-22T20:00:00Z"), shot.TakenAt);
        // Another step's picture is never shown under this one.
        Assert.Equal(ShotRead.Failed, ChromeShots.Read(200, Answer("s2", Webp), "s1").Read);
        // Another hash, not WebP, too big, no time, a bad time, padded base64: no picture.
        Assert.Equal(ShotRead.Failed, ChromeShots.Read(200, Answer("s1", Webp, sha: new string('0', 64)), "s1").Read);
        byte[] png = [0x89, .. "PNG"u8, 13, 10, 26, 10, .. "0000000000"u8];
        Assert.Equal(ShotRead.Failed, ChromeShots.Read(200, Answer("s1", png), "s1").Read);
        var big = Webp.Concat(new byte[ChromeShots.Largest]).ToArray();
        Assert.Equal(ShotRead.Failed, ChromeShots.Read(200, Answer("s1", big), "s1").Read);
        Assert.Equal(ShotRead.Failed, ChromeShots.Read(200, Answer("s1", Webp, takenAt: null), "s1").Read);
        Assert.Equal(ShotRead.Failed, ChromeShots.Read(200, Answer("s1", Webp, takenAt: "yesterday"), "s1").Read);
        Assert.Equal(ShotRead.Failed, ChromeShots.Read(200, Answer("s1", Webp, image: Convert.ToBase64String(Webp) + "="), "s1").Read);
        Assert.Equal(ShotRead.Failed, ChromeShots.Read(200, "not json", "s1").Read);
        // None kept is said as such; any other refusal is a failed read.
        Assert.Equal(ShotRead.None, ChromeShots.Read(404, "", "s1").Read);
        Assert.Equal(ShotRead.Failed, ChromeShots.Read(503, "", "s1").Read);
        Assert.Equal(ShotRead.Failed, ChromeShots.Read(403, "", "s1").Read);
    }
}

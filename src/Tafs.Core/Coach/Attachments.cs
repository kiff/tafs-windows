using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>Where a file's bytes go, as tafs cloud answers a declare: a PUT (as it says) to its link, with its headers, straight to storage.</summary>
public sealed record FileUpload(Uri Url, string Method, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// What a person may attach to a message, and making it ready (kiff/tafs#209, docs/attachments.md), as the iPad's
/// <c>FilePrep</c>: the types and limits kiff/agents keeps, checked here first so a file over its limit is refused before any
/// upload; five a message. Every image goes as a JPEG redrawn on this PC (<see cref="LongEdge"/>), so it carries only its pixels.
/// </summary>
public static class FilePrep
{
    public const int PerMessage = 5;

    /// <summary>A photo's long edge, at most: the most the model reads.</summary>
    public const int LongEdge = 2576;

    const int Mb = 1 << 20;

    public static readonly IReadOnlyDictionary<string, int> Limits = new Dictionary<string, int>
    {
        ["image/jpeg"] = 10 * Mb, ["image/png"] = 10 * Mb, ["image/gif"] = 10 * Mb, ["image/webp"] = 10 * Mb,
        ["application/pdf"] = 25 * Mb,
        ["text/plain"] = 2 * Mb, ["text/csv"] = 2 * Mb, ["text/markdown"] = 2 * Mb,
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = 2 * Mb,
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = 2 * Mb,
    };

    /// <summary>The extensions the picker offers, each with the media type tafs sends it as.</summary>
    public static readonly IReadOnlyDictionary<string, string> ByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif", [".webp"] = "image/webp",
        [".heic"] = "image/heic", [".bmp"] = "image/bmp", [".tif"] = "image/tiff", [".tiff"] = "image/tiff",
        [".pdf"] = "application/pdf", [".txt"] = "text/plain", [".csv"] = "text/csv", [".md"] = "text/markdown", [".markdown"] = "text/markdown",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    /// <summary>Whether a file is an image: it's redrawn as a JPEG before it goes, whatever its own format.</summary>
    public static bool IsImage(string name) => MediaType(name)?.StartsWith("image/", StringComparison.Ordinal) == true;

    /// <summary>The media type a file goes as, by its extension; none when it can't be attached.</summary>
    public static string? MediaType(string name) => ByExtension.GetValueOrDefault(Path.GetExtension(name));

    /// <summary>Why a file can't go, in the app's words; none when it can: its kind, then its type's limit.</summary>
    public static string? Problem(WordBook w, string name, long size, string? mediaType)
    {
        if (mediaType is null) return w.Get("attachment.wrong_kind", ("name", name));
        if (!Limits.TryGetValue(mediaType, out var limit)) return w.Get("attachment.cant_attach", ("name", name));
        return size > limit ? w.Get("attachment.too_large", ("name", name), ("size", Megabytes(size)), ("limit", Megabytes(limit))) : null;
    }

    public static string Megabytes(long n) => (n / (double)Mb).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

    /// <summary>A redrawn image's name: its own, as a .jpg; "Photo.jpg" when it has none.</summary>
    public static string JpegName(WordBook w, string name) =>
        (Path.GetFileNameWithoutExtension(name) is { Length: > 0 } stem ? stem : w.Get("attachment.photo")) + ".jpg";

    /// <summary>The size an image is redrawn at: its long edge at most <see cref="LongEdge"/>, never larger than it is.</summary>
    public static (uint Width, uint Height) Scaled(uint width, uint height, int edge = LongEdge)
    {
        var longest = Math.Max(width, height);
        if (longest <= edge || longest == 0) return (width, height);
        var scale = edge / (double)longest;
        return ((uint)Math.Max(1, Math.Round(width * scale)), (uint)Math.Max(1, Math.Round(height * scale)));
    }

    /// <summary>A file's SHA-256, in lowercase hex, as its upload is signed for.</summary>
    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>How many more files this message may take.</summary>
    public static int Room(int already) => Math.Max(0, PerMessage - already);
}

/// <summary>
/// Attaching a file in a chat, as the iPad's <c>TafsCloud.attach</c>, <c>upload</c> and <c>attachment</c>: declare it to tafs
/// cloud (<c>POST /v1/tafs/{id}/attachments</c>), which answers the file's record and where to upload its bytes; upload them
/// there with no session; open a sent one through a link that works for a minute.
/// </summary>
public static class Attachments
{
    public static string Path(string taf) => "/v1/tafs/" + CloudRead.PathPart(taf) + "/attachments";

    public static string FilePath(string taf, string file) => Path(taf) + "/" + CloudRead.PathPart(file);

    public static byte[] DeclareBody(string mediaType, string name, int size, string sha256) =>
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["media_type"] = mediaType, ["name"] = name, ["size"] = size, ["sha256"] = sha256 });

    /// <summary>
    /// The declare's answer: the file as the thread will name it, and where its bytes go. None when it isn't one, or its link
    /// isn't https: a file's bytes never go over plain HTTP.
    /// </summary>
    public static (ChatFile File, FileUpload Upload)? Declared(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || Lenient.Field(a, "attachment") is not { ValueKind: JsonValueKind.Object } f
            || Lenient.Field(a, "upload") is not { ValueKind: JsonValueKind.Object } u) return null;
        if (Lenient.Text(f, "id") is not { } id || Lenient.Text(u, "url") is not { } url
            || !Uri.TryCreate(url, UriKind.Absolute, out var link) || link.Scheme != Uri.UriSchemeHttps) return null;
        var headers = Lenient.Field(u, "headers") is { ValueKind: JsonValueKind.Object } h
            ? h.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!)
            : new Dictionary<string, string>();
        var file = new ChatFile(id, Lenient.Text(f, "kind") ?? "", Lenient.Text(f, "media_type") ?? "", Lenient.Text(f, "name") ?? "", Lenient.Int(f, "size") ?? 0);
        return (file, new FileUpload(link, Lenient.Text(u, "method") ?? "PUT", headers));
    }

    /// <summary>The download link a sent file opens through, https only; none when it isn't one.</summary>
    public static Uri? Link(JsonElement a) =>
        a.ValueKind == JsonValueKind.Object && Lenient.Text(a, "url") is { } url && Uri.TryCreate(url, UriKind.Absolute, out var link)
        && link.Scheme == Uri.UriSchemeHttps ? link : null;
}

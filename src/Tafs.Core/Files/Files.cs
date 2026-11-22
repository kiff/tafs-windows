using System.Globalization;
using System.Text;
using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Files;

/// <summary>
/// A file a taf delivered in a run, as tafs cloud lists it (<c>GET /v1/files</c>, <c>contracts/files-list.json</c>), drawn as the
/// iPad's <c>DeliveredFile</c>: its type is its name's extension (else its media type's), upper case; its run's time is when
/// it was delivered; it goes when tafs cloud says it expires; its taf's name is none once tafs has none for it.
/// </summary>
public sealed record DeliveredFile(string Id, string Name, string Type, long Bytes, string TafId, string TafName, string RunId,
    DateTimeOffset RunAt, string? Chat, DateTimeOffset GoesAt);

/// <summary>A file's start as tafs cloud sends it: its first 4 KB as plain text, and whether there's more.</summary>
public sealed record FilePreviewText(string Text, bool Truncated);

/// <summary>What a file's screen draws above its buttons, as the iPad's <c>FilesModel.PreviewRead</c>.</summary>
public enum PreviewKind
{
    /// <summary>tafs cloud's cleaned start, drawn as plain text.</summary>
    Text,
    /// <summary>The bytes on this PC, drawn here: only for a type with no text start.</summary>
    Copy,
    /// <summary>tafs cloud answered a text type with no start: no preview, and no bytes read.</summary>
    NoPreview,
    /// <summary>tafs cloud couldn't be reached for a text type: the line, and no bytes read.</summary>
    Unreachable,
}

/// <summary>A file's preview read: its kind, the text start for <see cref="PreviewKind.Text"/>, the bytes for <see cref="PreviewKind.Copy"/>.</summary>
public sealed record PreviewRead(PreviewKind Kind, FilePreviewText? Start = null, byte[]? Bytes = null);

/// <summary>
/// Files (docs/files.md): what the account's tafs delivered in their runs, kept 30 days from the run and seen by everyone on
/// the account, as the iPad's <c>Files.swift</c> and <c>TafsCloud+Files.swift</c>. tafs cloud's facts, read leniently; nothing
/// decided here.
/// </summary>
public static class DeliveredFiles
{
    public const string ListPath = "/v1/files";

    /// <summary>At most this many pages are read at once: a month of an account's files, well past what a screen shows.</summary>
    public const int Pages = 10;

    /// <summary>The types tafs cloud gives a text start for: their bytes are never drawn, even on a failed read.</summary>
    public static readonly IReadOnlySet<string> TextTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CSV", "MD", "TXT", "JSON" };

    /// <summary>The types this PC draws from their bytes, as pictures; any other type has no preview, only its details.</summary>
    public static readonly IReadOnlySet<string> PictureTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PNG", "JPG", "JPEG", "GIF", "BMP" };

    public static string PagePath(string? after) => after is null ? ListPath : $"{ListPath}?after={Uri.EscapeDataString(after)}";
    public static string FilePath(string id) => $"{ListPath}/{Uri.EscapeDataString(id)}";
    public static string DownloadPath(string id) => FilePath(id) + "/download";

    /// <summary>One page: its files (an entry without an id, taf, run, name or times is left out) and the next page's key; null when it isn't a page.</summary>
    public static (IReadOnlyList<DeliveredFile> Files, string? Next)? ReadPage(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object || Lenient.Field(answer, "files") is not { ValueKind: JsonValueKind.Array } list) return null;
        var files = list.EnumerateArray().Select(One).OfType<DeliveredFile>().ToList();
        return (files, Lenient.Text(answer, "next") is { Length: > 0 } next ? next : null);
    }

    static DeliveredFile? One(JsonElement f)
    {
        if (f.ValueKind != JsonValueKind.Object || Lenient.Text(f, "id") is not { Length: > 0 } id || Lenient.Text(f, "taf") is not { } taf
            || Lenient.Text(f, "run") is not { } run || Lenient.Text(f, "name") is not { } raw
            || Lenient.At(f, "created") is not { } created || Lenient.At(f, "expires") is not { } expires) return null;
        var name = FileText.Name(raw, id);
        var ext = Path.GetExtension(name).TrimStart('.');
        var type = ext.Length > 0 ? ext : (Lenient.Text(f, "media_type") ?? "").Split('/').Last();
        return new(id, name, type.ToUpperInvariant(), Lenient.Long(f, "size") ?? 0, taf, Lenient.Text(f, "taf_name") ?? "", run, created,
            Lenient.Text(f, "chat"), expires);
    }

    /// <summary>One file's start (<c>contracts/files-file.json</c>), cleaned again here; null when tafs cloud gave none.</summary>
    public static FilePreviewText? ReadStart(JsonElement answer) =>
        answer.ValueKind == JsonValueKind.Object && Lenient.Text(answer, "preview") is { } text
            ? new(FileText.Preview(text), Lenient.Bool(answer, "preview_truncated")) : null;

    /// <summary>Where the bytes are, for a minute (<c>contracts/files-download.json</c>): https with a host and no credentials in it, else none.</summary>
    public static Uri? ReadDownload(JsonElement answer) =>
        answer.ValueKind == JsonValueKind.Object && Lenient.Text(answer, "url") is { } url && Uri.TryCreate(url, UriKind.Absolute, out var u)
            && u.Scheme == Uri.UriSchemeHttps && u.UserInfo.Length == 0 && u.Host.Length > 0 ? u : null;

    /// <summary>
    /// Every file, page by page (at most <see cref="Pages"/>), newest first; null with why when a page couldn't be read, so a
    /// failed read keeps nothing it had.
    /// </summary>
    public static async Task<(IReadOnlyList<DeliveredFile>? Files, string? Why)> LoadAsync(Func<string, Task<CloudRead>> read)
    {
        var all = new List<DeliveredFile>();
        string? after = null;
        for (var i = 0; i < Pages; i++)
        {
            var answer = await read(PagePath(after));
            if (answer.Answer is not { } a || ReadPage(a) is not { } page) return (null, answer.Why ?? "");
            all.AddRange(page.Files);
            if (page.Next is not { } next) break;
            after = next;
        }
        return (NewestFirst(all), null);
    }

    /// <summary>
    /// A file's preview, as the iPad's <c>readPreview</c>: tafs cloud's start when it gives one. The bytes are read only for a
    /// type with no text start, and only for a picture this PC draws; a text type never has its bytes drawn.
    /// </summary>
    public static async Task<PreviewRead> PreviewAsync(DeliveredFile file, Func<string, Task<CloudRead>> read, Func<string, Task<byte[]?>> bytes)
    {
        var text = TextTypes.Contains(file.Type);
        var answer = await read(FilePath(file.Id));
        if (answer.Answer is null && text) return new(PreviewKind.Unreachable);
        if (answer.Answer is { } a && ReadStart(a) is { } start) return new(PreviewKind.Text, start);
        if (text || !PictureTypes.Contains(file.Type)) return new(PreviewKind.NoPreview);
        return await bytes(file.Id) is { } got ? new(PreviewKind.Copy, Bytes: got) : new(PreviewKind.Unreachable);
    }

    /// <summary>The files of one taf, one run or one chat (tafs cloud's <c>?taf=</c>, <c>?run=</c>, <c>?chat=</c>).</summary>
    public static IReadOnlyList<DeliveredFile> Of(IReadOnlyList<DeliveredFile> all, string? taf = null, string? run = null, string? chat = null) =>
        all.Where(f => (taf is null || f.TafId == taf) && (run is null || f.RunId == run) && (chat is null || f.Chat == chat)).ToList();

    /// <summary>Newest first; the name breaks a tie, so the order never moves between reads.</summary>
    public static IReadOnlyList<DeliveredFile> NewestFirst(IEnumerable<DeliveredFile> files) =>
        files.OrderByDescending(f => f.RunAt).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();

    /// <summary>Under the day of their run in the account's zone (<c>yyyy-MM-dd</c>), newest first.</summary>
    public static IReadOnlyList<(string Key, IReadOnlyList<DeliveredFile> Files)> ByDay(IEnumerable<DeliveredFile> files, TimeZoneInfo zone) =>
        Grouped(NewestFirst(files), f => TimeZoneInfo.ConvertTime(f.RunAt, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>Each taf with its files, the taf with the newest run first.</summary>
    public static IReadOnlyList<(string Key, IReadOnlyList<DeliveredFile> Files)> ByTaf(IEnumerable<DeliveredFile> files) => Grouped(NewestFirst(files), f => f.TafId);

    /// <summary>Each run with its files, the newest run first.</summary>
    public static IReadOnlyList<(string Key, IReadOnlyList<DeliveredFile> Files)> ByRun(IEnumerable<DeliveredFile> files) => Grouped(NewestFirst(files), f => f.RunId);

    static IReadOnlyList<(string, IReadOnlyList<DeliveredFile>)> Grouped(IReadOnlyList<DeliveredFile> files, Func<DeliveredFile, string> key) =>
        files.GroupBy(key).Select(g => (g.Key, (IReadOnlyList<DeliveredFile>)g.ToList())).ToList();

    /// <summary>A group's day, as the iPad's header: the weekday, the day and the month, short, in the account's zone ("Thu 19 Nov").</summary>
    public static string Day(DateTimeOffset at, TimeZoneInfo zone, CultureInfo culture) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("ddd d MMM", culture);

    /// <summary>Whole days left before it goes, never below none: 0 is today.</summary>
    public static int DaysLeft(DeliveredFile file, DateTimeOffset now) => Math.Max(0, (int)Math.Floor((file.GoesAt - now).TotalDays));

    /// <summary>When it goes, as the iPad's <c>FileDays.goes</c>: today, tomorrow, or in so many days.</summary>
    public static string Goes(WordBook w, int days) => days switch
    {
        0 => w.Get("files.goes_today"),
        1 => w.Get("files.goes_tomorrow"),
        _ => w.Count("files.goes_in_days", days),
    };

}

/// <summary>
/// A file's name and its preview as the app draws them, as the iPad's <c>FileText</c>: plain text only, never a path, and no
/// control or direction character, even if tafs cloud's own cleaning ever let one through.
/// </summary>
public static class FileText
{
    /// <summary>U+202A–U+202E, U+2066–U+2069, and the marks U+200E, U+200F and U+061C.</summary>
    public static bool IsDirection(int c) => c is >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069 or 0x200E or 0x200F or 0x061C;

    /// <summary>The name alone: after the last <c>/</c> or <c>\</c>, without control or direction characters; <paramref name="fallback"/> if nothing's left, or only "." or "..".</summary>
    public static string Name(string raw, string fallback)
    {
        var last = raw.Split('/', '\\').LastOrDefault(p => p.Length > 0) ?? "";
        var clean = Keep(last, c => !IsDirection(c) && Rune.GetUnicodeCategory(new Rune(c)) != UnicodeCategory.Control).Trim();
        return clean.Length == 0 || clean is "." or ".." ? fallback : clean;
    }

    /// <summary>Windows' device names: never a file's name on disk, whatever follows them.</summary>
    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// A name to write on this PC's disk (agentgabo's read): <see cref="Name"/>, then every character Windows refuses in a file's
    /// name made <c>_</c> (so <c>C:evil.exe</c> is no drive-relative path and <c>a:b</c> no alternate stream), trailing dots and
    /// spaces trimmed, and a device name (<c>CON</c>, <c>NUL.txt</c>) prefixed; <paramref name="fallback"/> if nothing's left.
    /// </summary>
    public static string DiskName(string raw, string fallback)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { ':', '*', '?', '"', '<', '>', '|', '/', (char)92 };
        var name = new string(Name(raw, fallback).Select(c => invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
        if (name.Length == 0 || name is "." or "..") name = fallback;
        var stem = name.Split('.')[0].TrimEnd(' ');
        return Reserved.Contains(stem) ? "_" + name : name;
    }

    /// <summary>
    /// Where a file's copy goes under <paramref name="root"/>: its own folder (the id, made a disk name), then the file's disk name;
    /// none when the path wouldn't stay inside that folder, so nothing is written then.
    /// </summary>
    public static string? CopyPath(string root, string id, string name)
    {
        var top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var folder = Path.GetFullPath(Path.Combine(top, DiskName(id, "file")));
        var path = Path.GetFullPath(Path.Combine(folder, DiskName(name, DiskName(id, "file"))));
        return folder.StartsWith(top, StringComparison.OrdinalIgnoreCase) && folder.Length > top.Length
            && path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    /// <summary>A preview's text: lines and tabs kept, Windows' line ends made one, every other control or direction character taken out.</summary>
    public static string Preview(string raw)
    {
        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        return Keep(lines, c => c is '\n' or '\t' || (!IsDirection(c) && Rune.GetUnicodeCategory(new Rune(c)) != UnicodeCategory.Control));
    }

    static string Keep(string s, Func<int, bool> keep)
    {
        var b = new StringBuilder(s.Length);
        foreach (var r in s.EnumerateRunes()) if (keep(r.Value)) b.Append(r.ToString());
        return b.ToString();
    }
}

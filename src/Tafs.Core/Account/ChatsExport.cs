using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Account;

/// <summary>
/// Export my chats, as the iPad's <c>exportChats</c> (AppModel.swift) and <c>TafsCloud.exportChats</c>/<c>splitExport</c>:
/// the person's own chats, trials and notes in tafs, for their rights of access and portability, as two files,
/// <c>tafs-chats.json</c> and <c>tafs-chats.txt</c>. Kept as tafs cloud wrote them; never another person's.
/// </summary>
public static class ChatsExport
{
    public const string Path = "/v1/me/chats/export";
    public const string JsonFile = "tafs-chats.json";
    public const string TextFile = "tafs-chats.txt";

    /// <summary>
    /// Where the files are written, as the iPad's <c>exportedFiles</c>: under the temporary folder, replaced at each export
    /// and removed at every session change, so nothing of the account stays on this PC after it.
    /// </summary>
    public static string Folder => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tafs", "export");

    /// <summary>
    /// tafs cloud's answer split into its two files, as <c>splitExport</c>: <c>readable</c> taken out as the text (empty when
    /// it isn't text), the rest written as JSON, indented, its keys sorted, slashes and letters left as they are. An answer
    /// that isn't an object isn't an export.
    /// </summary>
    public static (string Json, string Text)? Split(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object) return null;
        var text = answer.TryGetProperty("readable", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : "";
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            foreach (var p in answer.EnumerateObject().Where(p => p.Name != "readable").OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(p.Name);
                Write(writer, p.Value);
            }
            writer.WriteEndObject();
        }
        // Lines end as the iPad's, whatever this PC's own line ending: a string's own line breaks are escaped in JSON.
        return (Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n"), text);
    }

    static void Write(Utf8JsonWriter writer, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(p.Name);
                    Write(writer, p.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in e.EnumerateArray()) Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(e.GetString());
                break;
            default:
                // Numbers, true, false and null as tafs cloud wrote them.
                writer.WriteRawValue(e.GetRawText(), skipInputValidation: true);
                break;
        }
    }

    /// <summary>
    /// The two files written in <paramref name="folder"/>, as <c>exportChats</c>: the folder removed and made again, so the
    /// last export goes. Their paths, JSON first. Windows refusing to write them throws.
    /// </summary>
    public static IReadOnlyList<string> Write(string folder, string json, string text)
    {
        Remove(folder);
        Directory.CreateDirectory(folder);
        var jsonPath = System.IO.Path.Combine(folder, JsonFile);
        var textPath = System.IO.Path.Combine(folder, TextFile);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(jsonPath, json, utf8);
        File.WriteAllText(textPath, text, utf8);
        return [jsonPath, textPath];
    }

    /// <summary>The last export's files gone, as the iPad's <c>removeOpenedFiles</c>; nothing there is fine.</summary>
    public static void Remove(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The export, as <c>exportChats</c>: tafs cloud's answer split and written. A read that didn't come, or isn't an export,
    /// says to check the connection; files that couldn't be written say this PC couldn't save them.
    /// </summary>
    public static async Task<(IReadOnlyList<string>? Files, string? Why)> RunAsync(WordBook w, Func<string, Task<Taf.CloudRead>> read, string folder)
    {
        var answer = (await read(Path)).Answer;
        if (answer is not { } a || Split(a) is not { } export) return (null, w.Get("connect.chats_were_not_exported_check"));
        try
        {
            return (Write(folder, export.Json, export.Text), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, w.Get("chat.chats_were_not_exported_could", ("device", w.Get("device.windows"))));
        }
    }

    /// <summary>"Ready: tafs-chats.json, tafs-chats.txt. Share again", naming the files the last export made.</summary>
    public static string Ready(WordBook w, IReadOnlyList<string> files) =>
        w.Get("export.ready_share_again", ("files", string.Join(", ", files.Select(System.IO.Path.GetFileName))));
}

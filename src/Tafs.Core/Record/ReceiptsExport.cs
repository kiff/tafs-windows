using System.Globalization;
using System.Text;
using System.Text.Json;
using Tafs.Core.Account;
using Tafs.Core.Taf;

namespace Tafs.Core.Record;

/// <summary>An export of receipts as tafs cloud answered it: its days, its answer byte for byte, its CSV, and the calls with no receipt yet.</summary>
public sealed record ReceiptsAnswer(string From, string To, byte[] Json, string Csv, int Missing);

/// <summary>
/// Export receipts, for someone who sees every taf's Record, as the iPad's <c>ExportReceiptsRow</c> and
/// <c>exportReceipts</c> (<c>GET /v1/record/receipts?from=&amp;to=</c>, at most 92 days): a range of the last 7, 30 or 90
/// days, then two files to share: tafs cloud's answer as it came, so KIFF's receipts are exactly as issued, never
/// re-encoded, and its CSV. tafs cloud decides who may (403).
/// </summary>
public static class ReceiptsExport
{
    /// <summary>The day ranges offered.</summary>
    public static readonly IReadOnlyList<int> Days = [7, 30, 90];

    /// <summary>The export's path, its days as <see cref="Range"/> writes them (digits and dashes only), sent as they are, as the iPad's.</summary>
    public static string Path(string from, string to) => $"/v1/record/receipts?from={from}&to={to}";

    /// <summary>The last <paramref name="count"/> days, today among them, as <c>YYYY-MM-DD</c>.</summary>
    public static (string From, string To) Range(int count, DateOnly today) =>
        (today.AddDays(-(count - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>tafs cloud's answer, its bytes kept as they came; its days, CSV and missing count read beside them. None for one that isn't an object.</summary>
    public static ReceiptsAnswer? Read(byte[] raw, string from, string to)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var a = doc.RootElement;
            if (a.ValueKind != JsonValueKind.Object) return null;
            return new(Lenient.Text(a, "from") ?? from, Lenient.Text(a, "to") ?? to, raw, Lenient.Text(a, "csv") ?? "", Lenient.Int(a, "missing") ?? 0);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Where the files are written: beside Export my chats', replaced at each export and removed at every session change.</summary>
    public static string Folder => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tafs", "receipts");

    /// <summary>The two files, <c>receipts-{from}-to-{to}.json</c> (the answer's own bytes) and <c>.csv</c>, as the iPad names them.</summary>
    public static IReadOnlyList<string> Write(string folder, ReceiptsAnswer export)
    {
        ChatsExport.Remove(folder);
        Directory.CreateDirectory(folder);
        var stem = $"receipts-{export.From}-to-{export.To}";
        var json = System.IO.Path.Combine(folder, stem + ".json");
        var csv = System.IO.Path.Combine(folder, stem + ".csv");
        File.WriteAllBytes(json, export.Json);
        File.WriteAllText(csv, export.Csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return [json, csv];
    }
}

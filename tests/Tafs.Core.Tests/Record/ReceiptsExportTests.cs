using System.Text;
using Tafs.Core.Record;

namespace Tafs.Core.Tests.Record;

/// <summary>Export receipts, as the iPad's ExportReceiptsRow and exportReceipts: the range, the answer's own bytes, its CSV.</summary>
[Part("record")]
public class ReceiptsExportTests
{
    [Fact]
    public void The_last_days_count_today_among_them()
    {
        Assert.Equal(("2026-11-10", "2026-11-16"), ReceiptsExport.Range(7, new DateOnly(2026, 11, 16)));
        Assert.Equal(("2026-08-19", "2026-11-16"), ReceiptsExport.Range(90, new DateOnly(2026, 11, 16)));
        Assert.Equal([7, 30, 90], ReceiptsExport.Days);
        Assert.Equal("/v1/record/receipts?from=2026-11-10&to=2026-11-16", ReceiptsExport.Path("2026-11-10", "2026-11-16"));
    }

    /// <summary>KIFF's receipts exactly as issued: the JSON file is the answer's own bytes, never written again.</summary>
    [Fact]
    public void The_answer_is_kept_as_it_came_and_its_CSV_beside_it()
    {
        var raw = Encoding.UTF8.GetBytes("""{"from":"2026-11-10","to":"2026-11-16","receipts":[{"sig":"<x>"}],"csv":"id,amount\n1,20\n","missing":2}""");
        var export = ReceiptsExport.Read(raw, "a", "b")!;
        Assert.Equal(("2026-11-10", "2026-11-16", 2), (export.From, export.To, export.Missing));
        var folder = Path.Combine(Path.GetTempPath(), "tafs-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var files = ReceiptsExport.Write(folder, export);
            Assert.Equal(["receipts-2026-11-10-to-2026-11-16.json", "receipts-2026-11-10-to-2026-11-16.csv"], files.Select(Path.GetFileName));
            Assert.Equal(raw, File.ReadAllBytes(files[0]));
            Assert.Equal("id,amount\n1,20\n", File.ReadAllText(files[1]));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

        // Its own days when tafs cloud doesn't say them; anything but an object is no export.
        Assert.Equal(("a", "b", "", 0), ReceiptsExport.Read("{}"u8.ToArray(), "a", "b") is { } e ? (e.From, e.To, e.Csv, e.Missing) : default);
        Assert.Null(ReceiptsExport.Read("[]"u8.ToArray(), "a", "b"));
        Assert.Null(ReceiptsExport.Read("nope"u8.ToArray(), "a", "b"));
    }
}

using Tafs.Core.Record;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Record;

/// <summary>The Record's reads of data folded by day, and the KIFF receipt line, as the iPad's RecordView.</summary>
[Part("record")]
public class RecordReadsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-17T12:00:00Z");

    static RecordEntry Entry(string title, int minutesAgo, bool read = false, string? receipt = null) =>
        new("Morning inbox", 1, EntryKind.Done, title, "within its Card · done", Now.AddMinutes(-minutesAgo),
            Call: new ActivityCall("p" + minutesAgo, title, "executed", Now.AddMinutes(-minutesAgo), false, ReadOnly: read,
                Receipt: receipt is null ? null : new CallReceipt("t" + minutesAgo, receipt)));

    [Fact]
    public void Under_All_a_day_s_reads_fold_apart_and_any_other_filter_shows_them()
    {
        RecordEntry[] entries = [Entry("list emails", 10), Entry("read rows", 20, read: true), Entry("list forms", 30, read: true)];
        var day = Assert.Single(RecordScreen.Days(En, entries, RecordFilter.All, null, Now, TimeZoneInfo.Utc, foldReads: true));
        Assert.Equal(["list emails"], day.Entries.Select(e => e.Title));
        Assert.Equal(["read rows", "list forms"], day.Reads!.Select(e => e.Title));
        Assert.Equal("· 2 reads of data, within its Card · Show", RecordScreen.ReadsLine(En, 2, open: false));
        Assert.Equal("· 1 read of data, within its Card · Hide", RecordScreen.ReadsLine(En, 1, open: true));
        // A taf's activity, and the Record under another filter, fold nothing.
        Assert.Equal(3, RecordScreen.Days(En, entries, RecordFilter.All, null, Now, TimeZoneInfo.Utc).Single().Entries.Count);
        Assert.Empty(RecordScreen.Days(En, entries, RecordFilter.Stopped, null, Now, TimeZoneInfo.Utc, foldReads: true).SelectMany(d => d.Reads ?? []));
    }

    [Fact]
    public void A_call_whose_receipt_KIFF_signed_says_KIFF_receipt()
    {
        Assert.Equal("KIFF receipt", RecordScreen.ReceiptLine(En, Entry("list emails", 1, receipt: "signed")));
        Assert.Null(RecordScreen.ReceiptLine(En, Entry("list emails", 1, receipt: "pending")));
        Assert.Null(RecordScreen.ReceiptLine(En, Entry("list emails", 1)));
    }
}

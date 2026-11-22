using System.Globalization;
using System.Text.Json;
using Tafs.Core.Files;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Files;

/// <summary>Files (docs/files.md), as the iPad's FilesTests: tafs cloud's pinned answers, the grouping, the days, the cleaning, and what's read for a preview.</summary>
[Part("record")]
public class FilesTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static JsonElement Contract(string name) => JsonDocument.Parse(File.ReadAllText(SharedFiles.Contract(name))).RootElement.GetProperty("answer");

    static DeliveredFile File1(string id, string taf, string run, string at, string name = "a.csv", string type = "CSV") =>
        new(id, name, type, 10, taf, taf.ToUpperInvariant(), run, DateTimeOffset.Parse(at, CultureInfo.InvariantCulture), null,
            DateTimeOffset.Parse(at, CultureInfo.InvariantCulture).AddDays(30));

    [Fact]
    public void Tafs_cloud_s_list_reads_as_the_screens_draw_it()
    {
        var (files, next) = DeliveredFiles.ReadPage(Contract("files-list.json"))!.Value;
        Assert.Null(next);
        Assert.Equal(new[] { "notes.md", "week.csv" }, files.Select(f => f.Name));
        var week = files[1];
        Assert.Equal(("CSV", 22L, "taf-0bde5000000000000000", "Order rows", "run-1", "chat-1"), (week.Type, week.Bytes, week.TafId, week.TafName, week.RunId, week.Chat));
        Assert.Equal(DateTimeOffset.Parse("2026-12-19T08:00:00Z", CultureInfo.InvariantCulture), week.GoesAt);
        Assert.Equal("MD", files[0].Type);
    }

    [Fact]
    public void A_file_s_start_and_its_download_read_from_the_pinned_answers()
    {
        Assert.Equal(new FilePreviewText("name,amount\nAna,12.50\n", false), DeliveredFiles.ReadStart(Contract("files-file.json")));
        Assert.Equal("s3.test", DeliveredFiles.ReadDownload(Contract("files-download.json"))!.Host);
        Assert.Null(DeliveredFiles.ReadDownload(JsonDocument.Parse("""{"url":"http://s3.test/x"}""").RootElement));
        Assert.Null(DeliveredFiles.ReadStart(JsonDocument.Parse("""{"id":"f"}""").RootElement));
    }

    [Fact]
    public void An_entry_missing_a_fact_is_left_out_and_the_type_falls_back_to_the_media_type()
    {
        var page = DeliveredFiles.ReadPage(JsonDocument.Parse("""
            {"files":[{"id":"f1","taf":"t","run":"r","name":"report","media_type":"application/pdf","size":5,"created":"2026-11-19T08:00:00Z","expires":"2026-12-19T08:00:00Z"},
                      {"id":"f2","taf":"t","run":"r","name":"x.csv","created":"2026-11-19T08:00:00Z"},
                      {"taf":"t","run":"r","name":"y.csv","created":"2026-11-19T08:00:00Z","expires":"2026-12-19T08:00:00Z"}],
             "next":"p2"}
            """).RootElement)!.Value;
        Assert.Equal("p2", page.Next);
        Assert.Equal(("f1", "PDF", ""), (page.Files.Single().Id, page.Files.Single().Type, page.Files.Single().TafName));
        Assert.Null(DeliveredFiles.ReadPage(JsonDocument.Parse("{}").RootElement));
    }

    [Fact]
    public void Names_are_never_a_path_and_carry_no_control_or_direction_character()
    {
        Assert.Equal("week.csv", FileText.Name("../../etc/week.csv", "f1"));
        Assert.Equal("week.csv", FileText.Name("C:\\Users\\x\\week.csv", "f1"));
        Assert.Equal("evilcod.exe", FileText.Name("evil\u202Edoc.exe".Replace("doc", "cod"), "f1"));
        Assert.Equal("f1", FileText.Name("..", "f1"));
        Assert.Equal("f1", FileText.Name("\u2066\u0007", "f1"));
        Assert.Equal("a\tb\nc\nd", FileText.Preview("a\tb\r\nc\rd\u200F\u0001"));
    }

    [Fact]
    public void Files_are_grouped_newest_first_by_day_taf_and_run()
    {
        var files = new[]
        {
            File1("1", "t1", "r1", "2026-11-18T23:30:00Z", "b.csv"),
            File1("2", "t2", "r2", "2026-11-19T08:00:00Z"),
            File1("3", "t1", "r1", "2026-11-18T23:30:00Z", "a.csv"),
        };
        Assert.Equal(new[] { "2", "3", "1" }, DeliveredFiles.NewestFirst(files).Select(f => f.Id));
        // 23:30 UTC is the 19th in Paris: one day there, two in UTC.
        Assert.Single(DeliveredFiles.ByDay(files, TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris")));
        Assert.Equal(2, DeliveredFiles.ByDay(files, TimeZoneInfo.Utc).Count);
        Assert.Equal(new[] { "t2", "t1" }, DeliveredFiles.ByTaf(files).Select(g => g.Key));
        Assert.Equal(new[] { "r2", "r1" }, DeliveredFiles.ByRun(files).Select(g => g.Key));
        Assert.Equal(new[] { "1", "3" }, DeliveredFiles.Of(files, taf: "t1").Select(f => f.Id));
    }

    [Fact]
    public void When_a_file_goes_counts_whole_days_and_says_today_tomorrow_or_in_so_many()
    {
        var f = File1("1", "t", "r", "2026-11-19T08:00:00Z");
        Assert.Equal(30, DeliveredFiles.DaysLeft(f, f.RunAt));
        Assert.Equal(0, DeliveredFiles.DaysLeft(f, f.GoesAt.AddHours(3)));
        Assert.Equal(0, DeliveredFiles.DaysLeft(f, f.GoesAt.AddHours(-3)));
        Assert.Equal("goes today", DeliveredFiles.Goes(En, 0));
        Assert.Equal("goes tomorrow", DeliveredFiles.Goes(En, 1));
        Assert.Equal("goes in 12 days", DeliveredFiles.Goes(En, 12));
    }

    [Fact]
    public void A_size_is_in_the_language_s_numbers()
    {
        Assert.Equal("22 B", DeliveredFiles.Size(22, CultureInfo.InvariantCulture));
        Assert.Equal("2 KB", DeliveredFiles.Size(1500, CultureInfo.GetCultureInfo("en")));
        Assert.Equal("1.5 MB", DeliveredFiles.Size(1_500_000, CultureInfo.GetCultureInfo("en")));
        Assert.Equal("1,5 MB", DeliveredFiles.Size(1_500_000, CultureInfo.GetCultureInfo("fr")));
        Assert.Equal("120 MB", DeliveredFiles.Size(120_000_000, CultureInfo.GetCultureInfo("en")));
    }

    [Fact]
    public async Task Every_page_is_read_and_a_failed_page_keeps_nothing()
    {
        var asked = new List<string>();
        Task<CloudRead> Pages(string path)
        {
            asked.Add(path);
            var next = path.EndsWith("p2") ? "" : ",\"next\":\"p2\"";
            var id = path.EndsWith("p2") ? "f2" : "f1";
            return Task.FromResult(new CloudRead(JsonDocument.Parse(
                $$"""{"files":[{"id":"{{id}}","taf":"t","run":"r","name":"{{id}}.csv","created":"2026-11-1{{(id == "f1" ? 8 : 9)}}T08:00:00Z","expires":"2026-12-19T08:00:00Z"}]{{next}}}""").RootElement));
        }
        var (files, why) = await DeliveredFiles.LoadAsync(Pages);
        Assert.Null(why);
        Assert.Equal(new[] { "f2", "f1" }, files!.Select(f => f.Id));
        Assert.Equal(new[] { "/v1/files", "/v1/files?after=p2" }, asked);

        var (none, refused) = await DeliveredFiles.LoadAsync(_ => Task.FromResult(new CloudRead(null, "tafs couldn't reach tafs cloud")));
        Assert.Null(none);
        Assert.Equal("tafs couldn't reach tafs cloud", refused);
    }

    [Fact]
    public async Task A_text_type_never_has_its_bytes_read_and_a_picture_is_drawn_from_them()
    {
        var bytesRead = 0;
        Task<byte[]?> Bytes(string _) { bytesRead++; return Task.FromResult<byte[]?>([1, 2, 3]); }
        Task<CloudRead> Start(string _) => Task.FromResult(new CloudRead(JsonDocument.Parse("""{"preview":"hi","preview_truncated":true}""").RootElement));
        Task<CloudRead> NoStart(string _) => Task.FromResult(new CloudRead(JsonDocument.Parse("""{"id":"f"}""").RootElement));
        Task<CloudRead> Unreached(string _) => Task.FromResult(new CloudRead(null, "no"));
        var csv = File1("1", "t", "r", "2026-11-19T08:00:00Z");
        var png = File1("2", "t", "r", "2026-11-19T08:00:00Z", "p.png", "PNG");
        var pdf = File1("3", "t", "r", "2026-11-19T08:00:00Z", "p.pdf", "PDF");

        Assert.Equal(new PreviewRead(PreviewKind.Text, new FilePreviewText("hi", true)), await DeliveredFiles.PreviewAsync(csv, Start, Bytes));
        Assert.Equal(PreviewKind.NoPreview, (await DeliveredFiles.PreviewAsync(csv, NoStart, Bytes)).Kind);
        Assert.Equal(PreviewKind.Unreachable, (await DeliveredFiles.PreviewAsync(csv, Unreached, Bytes)).Kind);
        Assert.Equal(PreviewKind.NoPreview, (await DeliveredFiles.PreviewAsync(pdf, NoStart, Bytes)).Kind);
        Assert.Equal(0, bytesRead);

        var picture = await DeliveredFiles.PreviewAsync(png, NoStart, Bytes);
        Assert.Equal((PreviewKind.Copy, 3), (picture.Kind, picture.Bytes!.Length));
        Assert.Equal(PreviewKind.Copy, (await DeliveredFiles.PreviewAsync(png, Unreached, Bytes)).Kind);
        Assert.Equal(PreviewKind.Unreachable, (await DeliveredFiles.PreviewAsync(png, NoStart, _ => Task.FromResult<byte[]?>(null))).Kind);
    }
}

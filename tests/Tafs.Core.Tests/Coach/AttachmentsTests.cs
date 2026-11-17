using System.Text;
using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Coach;

/// <summary>Photos and files on a message (kiff/tafs#209), as the iPad's FilePrep and TafsCloud.attach.</summary>
[Part("coach")]
public class AttachmentsTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    [Fact]
    public void Only_the_kinds_tafs_takes_go_and_each_under_its_limit()
    {
        Assert.Equal("application/pdf", FilePrep.MediaType("Invoice.PDF"));
        Assert.True(FilePrep.IsImage("photo.heic"));
        Assert.Null(FilePrep.MediaType("tool.exe"));
        Assert.Contains("can't be attached", FilePrep.Problem(En, "tool.exe", 10, null));
        Assert.Null(FilePrep.Problem(En, "a.pdf", 25 << 20, "application/pdf"));
        Assert.Equal("notes.txt is 2.5 MB; the most is 2.0 MB.", FilePrep.Problem(En, "notes.txt", (int)(2.5 * (1 << 20)), "text/plain"));
        Assert.Contains("can't be attached", FilePrep.Problem(En, "x.bmp", 10, "image/bmp"));
        Assert.Equal((0, 3), (FilePrep.Room(5), FilePrep.Room(2)));
    }

    /// <summary>Every image is redrawn as a JPEG, its long edge at most 2,576, never made larger, named for it.</summary>
    [Fact]
    public void An_image_is_redrawn_as_a_jpeg_no_larger_than_the_model_reads()
    {
        Assert.Equal((2576u, 1932u), FilePrep.Scaled(4032, 3024));
        Assert.Equal((1449u, 2576u), FilePrep.Scaled(3024, 5376));
        Assert.Equal((800u, 600u), FilePrep.Scaled(800, 600));
        Assert.Equal(("IMG_0042.jpg", "Photo.jpg"), (FilePrep.JpegName(En, "IMG_0042.HEIC"), FilePrep.JpegName(En, ".png")));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", FilePrep.Sha256(Encoding.ASCII.GetBytes("abc")));
    }

    [Fact]
    public void A_declare_answers_the_file_and_where_its_bytes_go_over_https_only()
    {
        Assert.Equal("""{"media_type":"application/pdf","name":"a.pdf","size":12,"sha256":"ab"}""",
            Encoding.UTF8.GetString(Attachments.DeclareBody("application/pdf", "a.pdf", 12, "ab")));
        var (file, upload) = Attachments.Declared(JsonDocument.Parse("""
            {"attachment": {"id": "att_1", "kind": "pdf", "media_type": "application/pdf", "name": "a.pdf", "size": 12},
             "upload": {"url": "https://files.example/up?sig=1", "method": "PUT", "headers": {"Content-Type": "application/pdf", "x-n": 3}}}
            """).RootElement)!.Value;
        Assert.Equal(("att_1", "pdf", 12), (file.Id, file.Kind, file.Size));
        Assert.Equal(("PUT", "application/pdf"), (upload.Method, upload.Headers["Content-Type"]));
        Assert.Single(upload.Headers);
        Assert.Null(Attachments.Declared(JsonDocument.Parse("""{"attachment": {"id": "a"}, "upload": {"url": "http://files.example/up"}}""").RootElement));
        Assert.Null(Attachments.Link(JsonDocument.Parse("""{"url": "http://x/y"}""").RootElement));
        Assert.Equal("/v1/tafs/t1/attachments/att%2F1", Attachments.FilePath("t1", "att/1"));
    }

    /// <summary>A message takes the ready files' ids; with none, its words alone, as before.</summary>
    [Fact]
    public void A_message_carries_its_ready_files()
    {
        Assert.Equal("""{"text":"See the invoice","attachments":["att_1","att_2"]}""",
            Encoding.UTF8.GetString(Tafs.Core.Taf.TafChatSend.Body("See the invoice", ["att_1", "att_2"])));
        Assert.Equal("""{"text":"Hi"}""", Encoding.UTF8.GetString(Tafs.Core.Taf.TafChatSend.Body("Hi", [])));
    }

    /// <summary>Coach's messages carry their files too, beside where they go.</summary>
    [Fact]
    public void A_message_to_Coach_carries_its_files()
    {
        Assert.Equal("""{"text":"Here","attachments":["att_1"],"new_chat":true,"new_chat_after_handover":true}""",
            Encoding.UTF8.GetString(CoachSend.Body("Here", CoachConversation.New, ["att_1"])));
        Assert.Equal("""{"text":"Here","session":"s1","new_chat_after_handover":true}""",
            Encoding.UTF8.GetString(CoachSend.Body("Here", CoachConversation.Of("s1"))));
    }

    /// <summary>Only the kinds tafs takes open, as their own media type, under an id that can't reach the path.</summary>
    [Theory]
    [InlineData("att_1", "a.pdf", "application/pdf", "att_1-a.pdf")]
    [InlineData("att_1", "Photo.jpg", "image/jpeg", "att_1-Photo.jpg")]
    [InlineData("att_1", "a.pdf.js", "application/pdf", null)]
    [InlineData("att_1", "a.lnk", "application/pdf", null)]
    [InlineData("att_1", "a.docm", "application/vnd.ms-word.document.macroEnabled.12", null)]
    [InlineData("att_1", "a", "application/pdf", null)]
    [InlineData("att_1", "a.pdf", "text/html", null)]
    [InlineData("..\\x", "a.pdf", "application/pdf", null)]
    [InlineData("C:\\x", "a.pdf", "application/pdf", null)]
    [InlineData("att_1", "..\\..\\a.pdf", "application/pdf", "att_1-a.pdf")]
    [InlineData("att_1", "../../a.pdf", "application/pdf", "att_1-a.pdf")]
    [InlineData("att_1", "a:b.pdf", "application/pdf", "att_1-ab.pdf")]
    public void Only_a_kind_tafs_takes_opens(string id, string name, string mediaType, string? opens) =>
        Assert.Equal(opens, Attachments.OpenableName(new ChatFile(id, "", mediaType, name, 1)));
}

using System.Text.Json;
using Tafs.Core.Account;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Account;

/// <summary>Account › Export my chats, as the iPad's exportChats and TafsCloud.splitExport.</summary>
[Part("account")]
public class ChatsExportTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    static string Folder() => Path.Combine(Path.GetTempPath(), "tafs-tests", Guid.NewGuid().ToString("N"), "export");

    /// <summary>readable is the text; the rest is JSON, indented, keys sorted at every level, slashes and letters as they are.</summary>
    [Fact]
    public void The_answer_is_split_into_its_two_files()
    {
        var (json, text) = ChatsExport.Split(Json("""
            {"tafs":[{"name":"Refunds","notes":[],"chats":[{"text":"a/b é","at":"2026-11-15T20:00:00Z"}]}],
             "readable":"Your chats in tafs.LINE","exported_at":"2026-11-15T20:00:00Z","n":2.50,"ok":true,"none":null}
            """.Replace("LINE", "\\n")))!.Value;
        Assert.Equal("Your chats in tafs.\n", text);
        Assert.Equal(string.Join("\n", [
            "{",
            "  \"exported_at\": \"2026-11-15T20:00:00Z\",",
            "  \"n\": 2.50,",
            "  \"none\": null,",
            "  \"ok\": true,",
            "  \"tafs\": [",
            "    {",
            "      \"chats\": [",
            "        {",
            "          \"at\": \"2026-11-15T20:00:00Z\",",
            "          \"text\": \"a/b é\"",
            "        }",
            "      ],",
            "      \"name\": \"Refunds\",",
            "      \"notes\": []",
            "    }",
            "  ]",
            "}"]), json);
    }

    /// <summary>An answer that isn't an object isn't an export; no readable is an empty text.</summary>
    [Fact]
    public void Only_an_object_is_an_export()
    {
        Assert.Null(ChatsExport.Split(Json("[]")));
        Assert.Null(ChatsExport.Split(Json("\"x\"")));
        Assert.Equal(("{}", ""), ChatsExport.Split(Json("{}")));
        Assert.Equal("", ChatsExport.Split(Json("""{"readable":3}"""))!.Value.Text);
    }

    /// <summary>The two files, JSON first; the last export replaced; and removed, with nothing there being fine.</summary>
    [Fact]
    public void The_files_are_written_replaced_and_removed()
    {
        var folder = Folder();
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "old.txt"), "last time");
        var files = ChatsExport.Write(folder, "{}", "words");
        Assert.Equal([Path.Combine(folder, "tafs-chats.json"), Path.Combine(folder, "tafs-chats.txt")], files);
        Assert.Equal(["tafs-chats.json", "tafs-chats.txt"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        Assert.Equal("words", File.ReadAllText(files[1]));
        Assert.Equal("Ready: tafs-chats.json, tafs-chats.txt. Share again", ChatsExport.Ready(En, files));
        ChatsExport.Remove(folder);
        Assert.False(Directory.Exists(folder));
        ChatsExport.Remove(folder);
    }

    /// <summary>The read is GET /v1/me/chats/export; one that fails, or isn't an export, says to check the connection.</summary>
    [Fact]
    public async Task A_read_that_fails_says_to_check_the_connection()
    {
        string? asked = null;
        var folder = Folder();
        var (files, why) = await ChatsExport.RunAsync(En, path => { asked = path; return Task.FromResult(CloudRead.Unreached(En)); }, folder);
        Assert.Equal("/v1/me/chats/export", asked);
        Assert.Null(files);
        Assert.Equal("Your chats weren't exported. Check your connection and try again.", why);
        Assert.Equal(why, (await ChatsExport.RunAsync(En, _ => Task.FromResult(CloudRead.Of(Json("[1]"))), folder)).Why);
        Assert.False(Directory.Exists(folder));
    }

    /// <summary>Files Windows won't write say this PC couldn't save them.</summary>
    [Fact]
    public async Task Files_that_cant_be_written_say_so()
    {
        var folder = Folder();
        Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
        File.WriteAllText(folder, "a file where the folder goes");
        var (files, why) = await ChatsExport.RunAsync(En, _ => Task.FromResult(CloudRead.Of(Json("""{"readable":"x"}"""))), folder);
        Assert.Null(files);
        Assert.Equal("Your chats weren't exported: this Windows PC couldn't save the files.", why);
    }

    /// <summary>An export that works gives both files.</summary>
    [Fact]
    public async Task An_export_gives_both_files()
    {
        var folder = Folder();
        var (files, why) = await ChatsExport.RunAsync(En, _ => Task.FromResult(CloudRead.Of(Json("""{"readable":"hello","tafs":[]}"""))), folder);
        Assert.Null(why);
        Assert.Equal("hello", File.ReadAllText(files![1]));
        Assert.Equal("{\n  \"tafs\": []\n}", File.ReadAllText(files[0]));
        ChatsExport.Remove(folder);
    }
}

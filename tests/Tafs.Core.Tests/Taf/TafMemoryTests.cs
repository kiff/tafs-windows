using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>Every note a taf keeps, and Forget, as the iPad's MemoryPanel opened from See all.</summary>
[Part("record")]
public class TafMemoryTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    [Fact]
    public void Every_note_is_read_and_one_it_cant_read_is_left_out()
    {
        var notes = TafMemory.Read(JsonDocument.Parse("""
            {"notes": [
              {"id": "n1", "path": "/customers/acme.md", "content": "Acme pays on the 5th.", "updated_at": "2026-11-17T09:00:00Z"},
              {"id": "n2", "path": "/how-you-like-it.md", "content": "One line, not a paragraph."},
              {"id": "n3", "path": "/@me/notes.md", "content": "Ask me first.", "updated_at": "2026-11-17T10:00:00Z"}]}
            """).RootElement)!;
        Assert.Equal(["n1", "n3"], notes.Select(n => n.Id));
        Assert.Equal("customers · acme · 09:00", TafSetup.NoteLine(notes[0], TimeZoneInfo.Utc));
        Assert.Null(TafMemory.Read(JsonDocument.Parse("""{"notes": null}""").RootElement));
        Assert.Equal("/v1/tafs/taf_1/memory?all=true", TafMemory.Path("taf_1"));
        Assert.Equal("/v1/tafs/taf_1/memory/n%2F1", TafMemory.NotePath("taf_1", "n/1"));
    }

    [Fact]
    public void Forgotten_or_already_gone_says_nothing_else_why_not()
    {
        Assert.Null(TafMemory.Forgotten(En, CloudRead.Of(JsonDocument.Parse("""{"forgotten": "n1"}""").RootElement)));
        Assert.Null(TafMemory.Forgotten(En, new CloudRead(null, "Not found.", 404)));
        Assert.Equal("It wasn't forgotten. Too busy.", TafMemory.Forgotten(En, new CloudRead(null, "Too busy.", 429)));
    }
}

using System.Text.Json;
using Tafs.Core.Account;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Account;

/// <summary>Account › Devices, read as the iPad's KIFFClient.Device, with "Last used …" as Apple's relative formatter writes it.</summary>
[Part("account")]
public class DevicesTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");
    static readonly DateTimeOffset Now = new(2026, 11, 15, 20, 0, 0, TimeSpan.Zero);

    static IReadOnlyList<Device>? Read(string json) => Devices.Read(JsonDocument.Parse(json).RootElement);

    /// <summary>KIFF's display name, else the sign-in's; last used, else last seen; this device false when not said.</summary>
    [Fact]
    public void Devices_read_as_the_iPad_s()
    {
        var list = Read("""
            {"devices":[
              {"id":"dev_1","display_name":"tafs on Windows","name":"DESKTOP-1","last_used_at":"2026-11-15T17:00:00Z","this_device":true},
              {"id":"dev_2","name":"Rae's iPhone","last_seen_at":"2026-11-14T20:00:00Z"},
              {"id":"dev_3","display_name":null,"name":null,"last_used_at":null}]}
            """)!;
        Assert.Equal(new Device("dev_1", "tafs on Windows", new DateTimeOffset(2026, 11, 15, 17, 0, 0, TimeSpan.Zero), true), list[0]);
        Assert.Equal(new Device("dev_2", "Rae's iPhone", new DateTimeOffset(2026, 11, 14, 20, 0, 0, TimeSpan.Zero), false), list[1]);
        Assert.Equal(("A device", "Not used yet"), (Devices.Name(En, list[2]), Devices.LastUsed(En, list[2].LastUsed, Now)));
        Assert.Equal("Last used 3 hours ago", Devices.LastUsed(En, list[0].LastUsed, Now));
        Assert.Equal("Last used 1 day ago", Devices.LastUsed(En, list[1].LastUsed, Now));
    }

    /// <summary>A device without its id, or a field of the wrong kind, fails the read, as the iPad's decode.</summary>
    [Theory]
    [InlineData("""{"devices":[{"name":"x"}]}""")]
    [InlineData("""{"devices":[{"id":"d","this_device":"yes"}]}""")]
    [InlineData("""{"devices":[{"id":"d","last_used_at":"yesterday"}]}""")]
    [InlineData("""{"devices":[{"id":"d","name":42}]}""")]
    [InlineData("""{"list":[]}""")]
    public void A_device_that_doesn_t_read_fails_the_read(string json) => Assert.Null(Read(json));

    /// <summary>Each unit, singular and plural, in each shipped language's words for a past time.</summary>
    [Theory]
    [InlineData("en", 30, "30 seconds ago")]
    [InlineData("en", 60, "1 minute ago")]
    [InlineData("en", 3 * 3600, "3 hours ago")]
    [InlineData("en", 2 * 86400, "2 days ago")]
    [InlineData("en", 8 * 86400, "1 week ago")]
    [InlineData("en", 61 * 86400, "2 months ago")]
    [InlineData("en", 800 * 86400, "2 years ago")]
    [InlineData("fr", 3600, "il y a 1 heure")]
    [InlineData("fr", 3 * 3600, "il y a 3 heures")]
    [InlineData("de", 2 * 86400, "vor 2 Tagen")]
    [InlineData("es", 3600, "hace 1 hora")]
    [InlineData("it", 3 * 3600, "3 ore fa")]
    [InlineData("pt", 61 * 86400, "há 2 meses")]
    [InlineData("nl", 3 * 3600, "3 uur geleden")]
    [InlineData("pl", 3600, "1 godzinę temu")]
    [InlineData("pl", 3 * 3600, "3 godziny temu")]
    [InlineData("pl", 5 * 3600, "5 godzin temu")]
    [InlineData("pl", 2 * 86400, "2 dni temu")]
    public void A_past_time_in_each_language(string language, int seconds, string words) =>
        Assert.Equal(words, RelativeTime.Ago(language, Now.AddSeconds(-seconds), Now));
}

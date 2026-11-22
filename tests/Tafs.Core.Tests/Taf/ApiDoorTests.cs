using System.Globalization;
using System.Text;
using System.Text.Json;
using Tafs.Core.Contracts;
using Tafs.Core.Taf;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.Taf;

/// <summary>A door "From another application" (docs/doors.md), as the iPad's APIDoorText and the contract tafs cloud pins.</summary>
[Part("record")]
public class ApiDoorTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    static Door Pinned() => TafDoors.Read(ContractFile.Answer(SharedFiles.Contract("doors-api.json")))!.Doors.Single();

    [Fact]
    public void Tafs_cloud_s_pinned_api_door_reads_its_keys_counts_and_last_request()
    {
        var door = Pinned();
        Assert.True(door.IsApi);
        Assert.Equal((37, 100), (ApiDoor.Today(door), door.Limit));
        Assert.Equal("37 of 100", ApiDoor.OfLimit(En, door));
        Assert.Equal(new ApiLastRequest("done", DateTimeOffset.Parse("2026-11-19T09:42:15Z", CultureInfo.InvariantCulture), "Done"), door.LastRequest);
        var (prod, staging) = (door.Keys[0], door.Keys[1]);
        Assert.Equal(("ERP production", "iXrW"), (prod.Name, prod.Last4));
        Assert.NotNull(prod.LastUsedAt);
        Assert.Null(staging.LastUsedAt);
        Assert.Equal("Made 19 Nov 2026, 08:00 · last used 19 Nov 2026, 09:42 · ends …iXrW", ApiDoor.KeyLine(En, prod, TimeZoneInfo.Utc));
        Assert.Equal("Made 19 Nov 2026, 10:00 · not used yet · ends …Qp0z", ApiDoor.KeyLine(En, staging, TimeZoneInfo.Utc));
        Assert.Equal("Last request 19 Nov 2026, 09:42 · Done", ApiDoor.LastRequestLine(En, door, TimeZoneInfo.Utc));
    }

    [Fact]
    public void The_last_request_shows_only_in_tafs_cloud_s_words()
    {
        var door = Pinned() with { LastRequest = new ApiLastRequest("failed", DateTimeOffset.UtcNow, null) };
        Assert.Null(ApiDoor.LastRequestLine(En, door, TimeZoneInfo.Utc));
    }

    [Fact]
    public void The_signed_bodies_are_exact_bytes()
    {
        Assert.Equal("{\"name\":\"ERP / prod “2”\"}", Encoding.UTF8.GetString(ApiDoor.NewKeyBody("ERP / prod “2”")));
        Assert.Equal("{\"name\":\"a\\\"b\"}", Encoding.UTF8.GetString(ApiDoor.NewKeyBody("a\"b")));
        Assert.Equal("{\"open\":true}", Encoding.UTF8.GetString(ApiDoor.ReopenBody()));
        Assert.Empty(ApiDoor.NoBody());
    }

    [Fact]
    public void A_key_s_name_is_trimmed_to_sixty_else_the_door_s_own()
    {
        var door = Pinned();
        Assert.Equal("From another application", ApiDoor.KeyName("   ", door));
        Assert.Equal("Production", ApiDoor.KeyName("  Production ", door));
        Assert.Equal(60, ApiDoor.KeyName(new string('x', 90), door).Length);
        Assert.Equal("…iXrW", ApiDoor.Name(door.Keys[0] with { Name = null }));
    }

    [Fact]
    public void The_key_shown_once_reads_only_from_a_whole_answer()
    {
        var issued = ApiDoor.ReadIssued(JsonDocument.Parse("""{"id":"key_3","key":"tafs_live_abc","last4":"_abc","made_at":"2026-11-19T10:00:00Z"}""").RootElement)!;
        Assert.Equal(("key_3", "tafs_live_abc", "_abc"), (issued.Id, issued.Key, issued.Last4));
        Assert.Null(ApiDoor.ReadIssued(JsonDocument.Parse("""{"id":"key_3","last4":"_abc"}""").RootElement));
    }

    [Fact]
    public void The_endpoint_is_whole_and_the_example_never_holds_a_key()
    {
        var endpoint = ApiDoor.Endpoint(Pinned(), new Uri("https://cloud.tafs.example/"));
        Assert.Equal("https://cloud.tafs.example/v1/doors/api/door_ord7/requests", endpoint);
        var example = ApiDoor.Example(endpoint);
        Assert.Contains("$TAFS_API_KEY", example);
        Assert.StartsWith("curl -X POST https://cloud.tafs.example/v1/doors/api/door_ord7/requests", example);
    }
}

using System.Text;
using System.Text.Json;
using Tafs.Core.Coach;
using Tafs.Core.Taf;

namespace Tafs.Core.Tests.Coach;

/// <summary>KIFF's alerts kept with tafs cloud at Start, as the iPad's keepKIFFAlerts.</summary>
[Part("coach")]
public class KiffAlertsTests
{
    static readonly Uri Cloud = new("https://cloud.tafs.test");
    static CloudRead Json(string s) => CloudRead.Of(JsonDocument.Parse(s).RootElement.Clone());

    sealed class Calls
    {
        public readonly List<string> Said = [];
        public CloudRead Kept = Json("""{"alerts": false}""");
        public (int, string) Registered = (201, """{"id": "at_1", "signing_secret": "whsec_x"}""");
        public CloudRead Handed = Json("""{"alerts": true}""");

        public Task<AlertTarget?> Keep(AlertTarget? pending = null, bool mayAnswer = true) => KiffAlerts.KeepAsync(mayAnswer, pending,
            () => { Said.Add("kept?"); return Task.FromResult(Kept); },
            body => { Said.Add("register " + Encoding.UTF8.GetString(body)); return Task.FromResult(Registered); },
            body => { Said.Add("hand over " + Encoding.UTF8.GetString(body)); return Task.FromResult(Handed); },
            KiffAlerts.Url(Cloud), () => true);
    }

    [Fact]
    public async Task Not_kept_yet_KIFF_s_target_goes_straight_to_tafs_cloud()
    {
        var calls = new Calls();
        Assert.Null(await calls.Keep());
        Assert.Equal([
            "kept?",
            """register {"url":"https://cloud.tafs.test/v1/kiff/alerts"}""",
            """hand over {"alert_target":{"id":"at_1","signing_secret":"whsec_x"}}""",
        ], calls.Said);
    }

    [Fact]
    public async Task Kept_already_unknown_or_not_for_someone_who_can_t_answer_nothing_is_made()
    {
        var kept = new Calls { Kept = Json("""{"alerts": true}""") };
        Assert.Null(await kept.Keep());
        Assert.Equal(["kept?"], kept.Said);

        var unknown = new Calls { Kept = new CloudRead(null, "offline") };
        Assert.Null(await unknown.Keep());
        Assert.Equal(["kept?"], unknown.Said);

        var viewer = new Calls();
        Assert.Null(await viewer.Keep(mayAnswer: false));
        Assert.Empty(viewer.Said);
    }

    [Fact]
    public async Task A_target_tafs_cloud_didn_t_take_is_handed_over_next_time_never_made_twice()
    {
        var first = new Calls { Handed = new CloudRead(null, "refused", 500) };
        var left = await first.Keep();
        Assert.Equal(new AlertTarget("at_1", "whsec_x"), left);
        Assert.DoesNotContain("whsec_x", left!.ToString());

        var next = new Calls();
        Assert.Null(await next.Keep(left));
        Assert.Equal(["""hand over {"alert_target":{"id":"at_1","signing_secret":"whsec_x"}}"""], next.Said);

        // KIFF refusing the target: nothing to hand over, nothing kept.
        var refused = new Calls { Registered = (403, "{}") };
        Assert.Null(await refused.Keep());
        Assert.DoesNotContain(refused.Said, s => s.StartsWith("hand over", StringComparison.Ordinal));
    }
}

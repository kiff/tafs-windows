using System.Text.Json;
using System.Text.Json.Nodes;
using Tafs.Core.Approvals;
using Tafs.Core.NeedsYou;
using Tafs.Core.Tests.Ci;
using Tafs.Core.Tests.Strings;

namespace Tafs.Core.Tests.NeedsYou;

/// <summary>
/// "Allow this from now on", its second half: kiff/tafs' <c>vectors/agent-draft-keys.json</c>, run over KIFF's own draft
/// (<c>contracts/kiff-agent-draft-from-hold.json</c>), as on the iPad, Android, macOS 12 and Linux.
/// </summary>
[Part("approvals")]
public class HoldAllowKeysTests
{
    static JsonElement Vector => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoFiles.Root, "shared", "vectors", "agent-draft-keys.json"))).RootElement;

    /// <summary>KIFF's draft as it answered, the vector's ground.</summary>
    static JsonNode Draft() => JsonNode.Parse(File.ReadAllText(SharedFiles.Contract("kiff-agent-draft-from-hold.json")))!["answer"]!.DeepClone();

    public static TheoryData<string> Cases()
    {
        var data = new TheoryData<string>();
        foreach (var c in Vector.GetProperty("cases").EnumerateArray()) data.Add(c.GetProperty("name").GetString()!);
        return data;
    }

    [Fact]
    public void The_keys_are_the_vector_s_object_by_object()
    {
        var keys = Vector.GetProperty("keys");
        Assert.Equal(keys.EnumerateObject().Select(p => p.Name).Order(), HoldAllowKeys.Keys.Keys.Order());
        foreach (var kind in keys.EnumerateObject())
            Assert.Equal(kind.Value.EnumerateArray().Select(k => k.GetString()!).Order(), HoldAllowKeys.Keys[kind.Name].Order());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_case_passes_or_is_refused_as_the_vector_says(string name)
    {
        var c = Vector.GetProperty("cases").EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        var draft = Draft();
        foreach (var set in c.GetProperty("set").EnumerateObject()) draft = Set(draft, set.Name, JsonNode.Parse(set.Value.GetRawText()));
        Assert.Equal(c.GetProperty("ok").GetBoolean(), HoldAllowKeys.Known(JsonDocument.Parse(draft.ToJsonString()).RootElement));
    }

    /// <summary>The offer KIFF's pinned draft was made from: Rae's €150 refunds raised to €320.</summary>
    static AllowOffer Offer() => AllowOffers.Read(JsonDocument.Parse("""
        {"offer":{"changes":[{"mandate_id":"rae-refund","kind":"per_action","before":15000,"after":32000,"unit":"EUR",
          "words_before":"Refunds up to €150 each","words_after":"Refunds up to €320 each","rounded":false}],
          "draft":"drf_fixed","confirm_key":"critical","send_held":true}}
        """).RootElement)!.Offer!;

    static bool Signable(JsonNode draft)
    {
        var json = JsonDocument.Parse(draft.ToJsonString()).RootElement;
        var read = KiffDrafts.Read(json);
        Assert.NotNull(read);
        return HoldAllowKeys.Known(json) && HoldAllow.Matches(read, Offer(), "rae", "exc_fixed", "refund");
    }

    [Fact]
    public void KIFF_s_pinned_draft_is_what_its_offer_says_and_is_signed() => Assert.True(Signable(Draft()));

    [Fact]
    public void A_key_KIFF_adds_later_still_matches_the_offer_but_is_never_signed()
    {
        var draft = Set(Draft(), "/cards/0/room_grants", new JsonArray("rae"));
        var read = KiffDrafts.Read(JsonDocument.Parse(draft.ToJsonString()).RootElement);
        Assert.NotNull(read);
        Assert.True(HoldAllow.Matches(read, Offer(), "rae", "exc_fixed", "refund"));
        Assert.False(Signable(draft));
    }

    /// <summary>Sets an RFC 6901 pointer's value, adding the last key when it's new; the root itself when it's the whole draft.</summary>
    static JsonNode Set(JsonNode root, string pointer, JsonNode? value)
    {
        var tokens = pointer.Split('/').Skip(1).Select(t => t.Replace("~1", "/").Replace("~0", "~")).ToList();
        var at = root;
        foreach (var t in tokens.SkipLast(1)) at = at is JsonArray a ? a[int.Parse(t)]! : at[t]!;
        var last = tokens[^1];
        if (at is JsonArray list) list[int.Parse(last)] = value;
        else at[last] = value;
        return root;
    }
}

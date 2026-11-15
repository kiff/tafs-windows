using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>
/// A taf's KIFF Cards as tafs cloud read them from KIFF (Home's <c>card.cards</c>, contracts/home.json), as the iPad's
/// KIFFMapping.taf. Nothing is worked out here: a total, a use or a count that can't be read is none, never zero.
/// </summary>
public static class TafCards
{
    /// <summary>Each Card on its own, in tafs cloud's order (the live ones first). A Card without an id is left out.</summary>
    public static IReadOnlyList<TafCard> Read(WordBook w, JsonElement card) =>
        Lenient.Array(card, "cards").Where(c => Lenient.Text(c, "id") is not null).Select(c => new TafCard(
            Lenient.Text(c, "id")!,
            Lenient.Text(c, "title") ?? "",
            Lenient.Array(c, "rules").Where(r => Lenient.Text(r, "action") is not null).Select(r => Rule(w, r)).ToList(),
            Total(w, Lenient.Field(c, "total")),
            Calls(w, Lenient.Field(c, "calls")),
            Lenient.Text(c, "environment") ?? "",
            Live: Lenient.Field(c, "live") is not { ValueKind: JsonValueKind.False },
            Active: Lenient.Field(c, "active") is not { ValueKind: JsonValueKind.False },
            Expires: Lenient.At(c, "expires"))).ToList();

    static CardRule Rule(WordBook w, JsonElement r) => new(
        Lenient.Text(r, "action")!,
        Lenient.Text(r, "label") ?? Lenient.Text(r, "action")!,
        Window(w, r),
        Amount(Lenient.Field(r, "per_action")),
        Amount(Lenient.Field(r, "per_window")),
        Lenient.Int(r, "count_per_window"),
        Outside: Lenient.Text(r, "outside") == "refuse" ? Outside.Refuse : Outside.Hold,
        ExtraRoom: Amount(Lenient.Field(r, "extra_room")),
        BaseLimit: Amount(Lenient.Field(r, "base_limit")),
        RoomEnds: Lenient.At(r, "room_window_end"));

    static CardTotal? Total(WordBook w, JsonElement? t) =>
        t is { ValueKind: JsonValueKind.Object } o && Amount(Lenient.Field(o, "limit")) is { } limit
            ? new(limit, Amount(Lenient.Field(o, "used")), Window(w, o), Amount(Lenient.Field(o, "extra_room")),
                Amount(Lenient.Field(o, "base_limit")), Lenient.At(o, "room_window_end"), Lenient.Text(o, "pending_text"),
                Lenient.Text(o, "unit_words"))
            : null;

    /// <summary>Its count of calls; <c>used</c> is null when KIFF couldn't read it.</summary>
    static CallsTotal? Calls(WordBook w, JsonElement? c) =>
        c is { ValueKind: JsonValueKind.Object } o && Lenient.Int(o, "limit") is { } limit
            ? new(limit, Lenient.Int(o, "used"), Window(w, o), Lenient.Int(o, "extra_room"), Lenient.Int(o, "base_limit"),
                Lenient.At(o, "room_window_end"), Lenient.Text(o, "pending_text"))
            : null;

    /// <summary>An amount as KIFF gives it, <c>{minor, unit, scale}</c>; none without its integer.</summary>
    static KiffAmount? Amount(JsonElement? a) =>
        a is { ValueKind: JsonValueKind.Object } o && Lenient.Long(o, "minor") is { } minor
            ? new(minor, Lenient.Text(o, "unit"), Lenient.Int(o, "scale") ?? 0)
            : null;

    /// <summary>KIFF's window, in tafs cloud's words when it sent them (<c>window_words: {each, now}</c>).</summary>
    static CardWindow Window(WordBook w, JsonElement o)
    {
        var words = Lenient.Field(o, "window_words") ?? default;
        return CardWindow.Of(w, Lenient.Text(o, "window") ?? "", Lenient.Text(words, "each"), Lenient.Text(words, "now"));
    }
}

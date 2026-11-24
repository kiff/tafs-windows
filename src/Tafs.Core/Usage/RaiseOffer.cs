using System.Text.Json;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Usage;

/// <summary>
/// tafs cloud's offer to raise the spend limit (the balance's <c>raise</c>), set only for someone who may raise it: its words
/// ("Raise the limit to €300"), the limit it's raised from (<c>from_cents</c>, which the raise signs), and that amount's currency
/// (<c>currency</c>, ISO 4217), which the app formats itself. Without the limit or a currency code, nothing is raised.
/// </summary>
public sealed record RaiseOffer(string Words, long? FromCents, string? Currency)
{
    /// <summary>The offer, leniently: no words, no offer; a <c>from_cents</c> that isn't a whole number, or a code that isn't one, is none.</summary>
    public static RaiseOffer? Read(JsonElement balance)
    {
        if (!balance.TryGetProperty("raise", out var r) || r.ValueKind != JsonValueKind.Object) return null;
        if (!r.TryGetProperty("words", out var w) || w.ValueKind != JsonValueKind.String || w.GetString() is not { Length: > 0 } words) return null;
        long? from = r.TryGetProperty("from_cents", out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetInt64(out var n) && n >= 0 ? n : null;
        var currency = r.TryGetProperty("currency", out var c) && c.ValueKind == JsonValueKind.String && Currencies.IsCode(c.GetString()) ? c.GetString() : null;
        return new RaiseOffer(words, from, currency);
    }

    /// <summary>
    /// What Raise's sheet says before Windows Hello: tafs cloud's offer (the new limit) and the limit it's raised from ("The
    /// spend limit is €200 now."), the signed <c>from_cents</c> in the currency tafs cloud names, at that currency's own
    /// decimals and in the app's language. None without both: nothing is raised then, never a guessed currency.
    /// </summary>
    public (string Offer, string From)? Lines(WordBook w)
    {
        if (FromCents is not { } cents || Currency is not { } code) return null;
        var amount = new KiffAmount(cents, code, Currencies.MinorDigits(code));
        return (Words, w.Get("usage.raise_limit.from", ("amount", amount.Figure(w))));
    }
}

/// <summary>Currency codes as tafs cloud sends them (ISO 4217): three capital letters, and how many decimals each one's amounts carry.</summary>
public static class Currencies
{
    /// <summary>Three capital letters A to Z, as ISO 4217 writes a code.</summary>
    public static bool IsCode(string? code) => code is { Length: 3 } && code.All(ch => ch is >= 'A' and <= 'Z');

    /// <summary>
    /// A currency's minor-unit digits (ISO 4217): what its smallest unit is worth, for an amount sent in it. Two unless ISO says
    /// otherwise. The same table as the other computers' apps.
    /// </summary>
    public static int MinorDigits(string code) => code switch
    {
        "BIF" or "CLP" or "DJF" or "GNF" or "ISK" or "JPY" or "KMF" or "KRW" or "PYG" or "RWF" or "UGX" or "UYI" or "VND" or "VUV"
            or "XAF" or "XOF" or "XPF" => 0,
        "BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
        "CLF" or "UYW" => 4,
        _ => 2,
    };
}

using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Coach;
using Tafs.Core.Kiff;
using Tafs.Core.Record;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.NeedsYou;

/// <summary>
/// "Allow this from now on" on a held call, as the iPad's <c>HoldAllow</c>. Whether to offer it is the app's own rule, from
/// the held call KIFF sent, with no request. What it offers is tafs cloud's. What's signed is KIFF's draft, which this PC
/// reads itself and signs only when it is what was offered.
/// </summary>
public static class HoldAllow
{
    /// <summary>The breaches a Card change can lift, as the iPad's <c>liftable</c>. A "never" is refused, not held.</summary>
    public static readonly IReadOnlySet<string> Liftable = new HashSet<string> { "per_action_exceeded", "limit_reached", "resource_not_on_card" };

    /// <summary>
    /// A hold from KIFF whose every breach a Card change can lift, as the iPad's <c>allowable</c>. A currency mismatch is
    /// never listed as one. The iPad also leaves out Coach's install hold: no held call marks it here, and
    /// <see cref="Offers"/> already keeps it out, since its taf is found by agent and Coach's agent is no listed taf's.
    /// </summary>
    public static bool Allowable(HeldCall call) =>
        call.KiffId is not null && call.BreachOutcomes is { Count: > 0 } outcomes && outcomes.All(Liftable.Contains);

    /// <summary>
    /// Whether "Allow this from now on" is live, as the iPad's <c>AppModel.allowIsLive</c>: the account's capabilities name
    /// <c>holds.allow</c>. Unlike the older capabilities, no list means not live.
    /// </summary>
    public static bool AllowIsLive(IReadOnlyList<string>? capabilities) => capabilities?.Contains("holds.allow") ?? false;

    /// <summary>
    /// Whether KIFF's draft is what was offered, as the iPad's <c>HoldAllow.matches</c>. The draft is for
    /// <paramref name="agent"/> (and, when KIFF says, made from held call <paramref name="hold"/>). It retires no Card and adds
    /// none. Each Card it changes is one the offer names, and reads exactly as that Card in force with the offered change
    /// made (<see cref="Applied"/>); every other term is the same (<see cref="Terms"/>). A Card marked unchanged must read as
    /// in force too. Anything else isn't what was offered, and nothing is signed.
    /// </summary>
    public static bool Matches(AgentDraft draft, AllowOffer offer, string? agent, string? hold, string action)
    {
        if (string.IsNullOrEmpty(agent) || draft.AgentId != agent) return false;
        if (draft.FromHold is { } from && from != hold) return false;
        if (draft.Retires is { Count: > 0 }) return false;
        var named = offer.Changes.Select(c => c.MandateId).ToHashSet();
        if (named.Count == 0 || draft.Cards is not { } cards) return false;
        var changed = new HashSet<string>();
        foreach (var card in cards)
        {
            if (card.Revises is not { } revises || card.Current is not { } current) return false;
            // A draft that sets how long a hold waits, or revokes, changes more than a limit.
            if (!string.IsNullOrEmpty(card.HoldExpiry) || card.RevokedAt is not null) return false;
            var inForce = new Terms(current);
            var terms = new Terms(card, current);
            if (card.Unchanged == true)
            {
                if (terms != inForce) return false;
                continue;
            }
            if (!named.Contains(revises) || changed.Contains(revises)) return false;
            var expected = new HashSet<Terms> { inForce };
            foreach (var change in offer.Changes.Where(c => c.MandateId == revises))
                expected = expected.SelectMany(t => Applied(t, change, action)).ToHashSet();
            if (!expected.Contains(terms)) return false;
            changed.Add(revises);
        }
        return changed.SetEquals(named);
    }

    /// <summary>
    /// Every way <paramref name="change"/> can apply to <paramref name="terms"/>, as the iPad's <c>HoldAllow.applied</c>: one
    /// limit of its kind, at its figure before and in its unit, moved to its figure after; or one value added to the scope of
    /// the held call's <paramref name="action"/>. A per-call cap is the held action's. A kind the contract doesn't name
    /// applies in no way.
    /// </summary>
    public static IReadOnlyList<Terms> Applied(Terms terms, AllowChange change, string action)
    {
        var all = new List<Terms>();
        switch (change.Kind)
        {
            case "per_action":
                if (change.Before is not { } before || change.After is not { } after) return [];
                for (var g = 0; g < terms.Grants.Count; g++)
                {
                    var grant = terms.Grants[g];
                    if (grant.Action != action) continue;
                    for (var c = 0; c < grant.Caps.Count; c++)
                    {
                        var cap = grant.Caps[c];
                        if (cap.Max != before || cap.Unit != change.Unit) continue;
                        var grants = terms.Grants.ToList();
                        grants[g] = grant with { Caps = Replaced(grant.Caps, c, cap with { Max = after }) };
                        all.Add(terms.With(grants: grants));
                    }
                }
                return all;
            case "total" or "count":
                if (change.Before is not { } from || change.After is not { } to) return [];
                for (var t = 0; t < terms.Totals.Count; t++)
                {
                    var total = terms.Totals[t];
                    if (total.Limit != from || total.Unit != change.Unit || (change.Window is not null && change.Window != total.Window)) continue;
                    all.Add(terms.With(totals: Replaced(terms.Totals, t, total with { Limit = to })));
                }
                return all;
            case "resource":
                // Exactly the values in force plus this one, on the held action's scope for this parameter.
                if (string.IsNullOrEmpty(change.Parameter) || string.IsNullOrEmpty(change.Value)) return [];
                for (var g = 0; g < terms.Grants.Count; g++)
                {
                    var grant = terms.Grants[g];
                    if (grant.Action != action || grant.ScopeOf != change.Parameter || grant.Scope.Contains(change.Value)) continue;
                    var grants = terms.Grants.ToList();
                    grants[g] = grant with { Scope = new([.. grant.Scope, change.Value]) };
                    all.Add(terms.With(grants: grants));
                }
                return all;
            default:
                return [];
        }
    }

    static TermList<T> Replaced<T>(TermList<T> list, int at, T item) => new(list.Select((x, i) => i == at ? item : x));

    /// <summary>KIFF's confirm of a draft, as the iPad's <c>KIFFClient.confirmDraft</c>.</summary>
    public static string ConfirmPath(string draft) => KiffDrafts.Path(draft) + "/confirm";

    /// <summary>
    /// The confirm's body: <c>{"version"}</c>, and with a held call <c>"hold": {"id", "accounting_version"}</c>, which KIFF lets
    /// go under the same signature (the iPad's <c>confirmDraft(id:version:hold:accountingVersion:)</c>).
    /// </summary>
    public static byte[] ConfirmBody(string version, (string Id, int AccountingVersion)? hold)
    {
        var body = new System.Text.Json.Nodes.JsonObject { ["version"] = version };
        if (hold is { } h) body["hold"] = new System.Text.Json.Nodes.JsonObject { ["id"] = h.Id, ["accounting_version"] = h.AccountingVersion };
        return Encoding.UTF8.GetBytes(body.ToJsonString());
    }

    /// <summary>
    /// The one signature and the confirm, as the iPad's <c>confirmAllow</c>: this PC's passkey asserts the confirm request
    /// itself (<paramref name="sign"/>), then it goes to KIFF (<paramref name="post"/>). Windows Hello closed sends nothing
    /// and says "Cancelled. Nothing changed.", as Refuse and Your step say it. Any other failure to sign (a refusal, Windows Hello or the TPM failing, an
    /// answer that can't be read) sends nothing and says why (<paramref name="why"/>), never leaving the sheet waiting.
    /// <c>Stopped</c> when another session began meanwhile: checked before Windows Hello is asked, as the iPad's
    /// <c>confirmAllow</c> returns with no account, and after each await. KIFF's refusal is said in the app's plain words for
    /// its status (<see cref="KiffErrors.Words"/>), as the iPad's <c>PlainError.words(status:)</c>, never KIFF's own text.
    /// </summary>
    public static async Task<(bool Done, string? Why, bool Stopped)> ConfirmAsync(WordBook w, string draft, byte[] body,
        Func<string, byte[], Task<(string Header, string Value)>> sign,
        Func<string, byte[], (string Header, string Value), Task<(int Status, string Body)>> post,
        Func<bool> current, Func<Exception, string> why)
    {
        if (!current()) return (false, null, true);
        var path = ConfirmPath(draft);
        (string Header, string Value) signature;
        try { signature = await sign(path, body); }
        catch (OperationCanceledException) { return current() ? (false, w.Get("approval.cancelled"), false) : (false, null, true); }
        catch (Exception e) when (e is Approvals.PasskeyRefusedException or InvalidDataException or System.Runtime.InteropServices.COMException
            or System.Security.Cryptography.CryptographicException or InvalidOperationException or UnauthorizedAccessException)
        {
            return (false, e is Approvals.PasskeyRefusedException ? e.Message : why(e), !current());
        }
        if (!current()) return (false, null, true);
        int status;
        try { (status, _) = await post(path, body, signature); }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException) { return (false, w.Get("error.offline"), !current()); }
        if (!current()) return (false, null, true);
        if (status is < 200 or > 299) return (false, KiffErrors.Words(w, status), false);
        return (true, null, false);
    }

    /// <summary>
    /// "Allow this from now on" on the card, as the iPad's <c>offersAllow</c>: once <c>holds.allow</c> is live, for someone who
    /// manages Cards, on a listed taf's hold a Card change can lift. Decided with no request.
    /// </summary>
    public static bool Offers(HeldCall call, IReadOnlyList<string>? capabilities, bool managesCards, string? taf) =>
        AllowIsLive(capabilities) && managesCards && !string.IsNullOrEmpty(taf) && Allowable(call);

    /// <summary>
    /// The confirm's one signature, as Start's Setup draft (<see cref="CoachStart.SignDraftAsync"/>): this PC's passkey
    /// asserts <c>kiff.request.v1</c> over exactly this request, its challenge base64url(sha256) of it. Only a session that
    /// approves with its passkey signs; any other throws <see cref="PasskeyRefusedException"/> before anything is asked.
    /// </summary>
    public static async Task<(string Header, string Value)> SignConfirmAsync(Kiff.KeptSession session, PasskeyApprover? passkey,
        string path, byte[] body, string signedAt, CancellationToken cancel = default)
    {
        if (!HeldAnswers.Approves(session.Mode, session.PasskeyId) || passkey is null)
            throw new PasskeyRefusedException("only this PC's passkey confirms a Card change");
        return (PasskeyApprover.HeaderName, await passkey.ApproveAsync(session.Tenant, "POST", path, body, signedAt, cancel));
    }

    /// <summary>
    /// tafs cloud's offer for a held call, then KIFF's draft, read by this PC, and whether the draft is what was offered, as
    /// the iPad's <c>HoldAllow.read</c>. A <c>none</c> reads no draft. Where the iPad throws, why instead: the service's
    /// refusal as each read says it, or "KIFF didn't answer as expected" for an answer that can't be read.
    /// </summary>
    /// <param name="post">tafs cloud's <c>POST</c> of a path and body.</param>
    /// <param name="get">KIFF's <c>GET</c> of a path, as the session.</param>
    /// <param name="current">Whether the session the sheet opened in is still signed in: signed out after the offer, KIFF's
    /// draft isn't read, as the iPad's <c>accountGeneration</c>, and nothing is said.</param>
    public static async Task<(AllowRead? Read, string? Why)> ReadAsync(WordBook w, string taf, HeldCall call, bool round,
        Func<string, byte[], Task<CloudRead>> post, Func<string, Task<CloudRead>> get, Func<bool>? current = null)
    {
        var offered = await post(AllowOffers.Path(taf, call.KiffId ?? ""), AllowOffers.Body(round));
        if (current is { } now && !now()) return (null, null);
        if (offered.Answer is not { } json) return (null, offered.Why);
        if (AllowOffers.Read(json) is not { } answer) return (null, w.Get("kiff.kiff_did_not_answer_expected"));
        if (answer.Offer is not { } offer || offer.Draft.Length == 0) return (new(answer, null, false), null);
        var kept = await get(KiffDrafts.Path(offer.Draft));
        if (kept.Answer is not { } d) return (null, kept.Why);
        if (KiffDrafts.Read(d) is not { } read) return (null, w.Get("kiff.kiff_did_not_answer_expected"));
        return (new(answer, read, Matches(read, offer, call.Agent, call.KiffId, call.Action ?? "")), null);
    }
}

/// <summary>What <see cref="HoldAllow.ReadAsync"/> read: tafs cloud's answer, KIFF's draft when there's an offer, and whether they match.</summary>
public sealed record AllowRead(AllowAnswer Answer, AgentDraft? Draft, bool Matches);

/// <summary>
/// "Allow this from now on", open on a held call, as the iPad's <c>AppModel.AllowSheet</c>: tafs cloud's answer, KIFF's draft
/// as this PC read it, whether it's what was offered, and the person's switch ("Let this one go out too", on unless the offer
/// says). Rounding up was asked for once <see cref="Rounded"/>, and the sheet no longer offers it.
/// </summary>
public sealed record AllowSheet(HeldCall Call, AllowAnswer? Answer = null, AgentDraft? Draft = null, bool Matches = false, bool Busy = false,
    string? Error = null, bool SendHeld = true, bool Rounded = false)
{
    /// <summary>Opened on a tap: tafs cloud makes the offer now, as the iPad's <c>openAllow</c>.</summary>
    public static AllowSheet Open(HeldCall call) => new(call, Busy: true);

    /// <summary>
    /// What was read, as the iPad's <c>loadAllow</c>: the answer, the draft and whether they match; the switch as the offer
    /// says on the first read, and as the person left it when rounding up.
    /// </summary>
    public AllowSheet Loaded(AllowRead read, bool round) => this with
    {
        Answer = read.Answer, Draft = read.Draft, Matches = read.Matches, Busy = false, Error = null,
        SendHeld = round ? SendHeld : read.Answer.Offer?.SendHeld ?? true, Rounded = round,
    };

    /// <summary>A read or a confirm that didn't go: "Couldn't read this version's Card." with why, as the iPad's.</summary>
    public AllowSheet Failed(WordBook w, string? why) => this with { Busy = false, Error = w.Get("version.card_not_read", ("why", why ?? "")) };

    /// <summary>KIFF's own lines for each Card the draft changes, as it keeps them: what's signed.</summary>
    public IReadOnlyList<string> KiffLines(WordBook w) => Draft is not { Cards: { } cards } draft ? []
        : cards.Zip(draft.CardLines(w)).Where(p => !p.First.IsUnchanged).SelectMany(p => p.Second).ToList();

    /// <summary>
    /// "This change isn't what was offered.": an offer whose draft doesn't match. A draft that couldn't be read says so in the
    /// error line alone.
    /// </summary>
    public bool NotOffered => Answer?.Offer is not null && !Matches && (Draft is not null || (!Busy && Error is null));

    /// <summary>"Round up to {amount}": a matching offer that names a rounded figure, not yet asked for.</summary>
    public string? RoundTo => Matches && !Rounded && Answer?.Offer?.Round?.Words is { Length: > 0 } words ? words : null;

    /// <summary>
    /// The confirm's body, as the iPad's <c>confirmAllow</c>: the draft's version as this PC read it, with the held call at the
    /// version the person saw when the switch is on. None when there's nothing to sign; with the switch on and no version,
    /// why ("KIFF doesn't take your answer to this one."), and nothing is signed.
    /// </summary>
    public (byte[]? Body, string? Why) Confirmation(WordBook w)
    {
        if (Draft is not { } draft || !Matches || Busy || draft.Confirmed) return (null, null);
        if (!SendHeld) return (HoldAllow.ConfirmBody(draft.Version, null), null);
        if (Call.KiffId is not { } id || Call.AccountingVersion is not { } version) return (null, w.Get("needs_you.answer_not_taken"));
        return (HoldAllow.ConfirmBody(draft.Version, (id, version)), null);
    }
}

/// <summary>
/// A list compared item by item, so the records holding it compare by value: <see cref="Terms"/> are equal when every term
/// is, as the iPad's <c>Hashable</c> structs are.
/// </summary>
public sealed class TermList<T> : IReadOnlyList<T>, IEquatable<TermList<T>>
{
    readonly T[] items;

    public TermList(IEnumerable<T> items) => this.items = items.ToArray();

    public T this[int index] => items[index];
    public int Count => items.Length;
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

    public bool Equals(TermList<T>? other) => other is not null && items.SequenceEqual(other.items);
    public override bool Equals(object? obj) => Equals(obj as TermList<T>);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in items) hash.Add(item);
        return hash.ToHashCode();
    }

    public override string ToString() => "[" + string.Join(", ", items) + "]";
}

/// <summary>
/// Every term of a Card that decides what its taf may do, in an order of their own, as the iPad's <c>HoldAllow.Terms</c>,
/// from a draft's Card or the Card in force. KIFF's lines and a grant's title are words, not terms. What a draft may leave
/// out reads as KIFF fills it: a total's measure (attempts for a count, effects for a sum), and the Card's holders when the
/// draft names only the same first holder.
/// </summary>
public sealed record Terms
{
    public sealed record Cap(string Quantity, long Max, string? Unit, int? Scale);

    /// <param name="ScopeOf">The scope's parameter; none for a grant with no scope.</param>
    /// <param name="Scope">The scope's values, sorted once in <see cref="Terms"/>.</param>
    public sealed record Grant(string Action, TermList<Cap> Caps, string? ScopeOf, TermList<string> Scope, TermList<string> States,
        TermList<string> NextSteps);

    public sealed record Total(string Quantity, long Limit, string Window, string? Unit, int? Scale, string Measure, string Per);

    public TermList<Grant> Grants { get; private init; }
    public TermList<Total> Totals { get; private init; }
    public string? OnExceed { get; private init; }
    public TermList<string> Holders { get; private init; }
    public string Domain { get; private init; }
    public string Environment { get; private init; }
    public string Location { get; private init; }
    /// <summary>When it starts and ends, as the instants KIFF's times name (<see cref="Instant"/>): "" for none.</summary>
    public string ValidFrom { get; private init; }
    public string ValidUntil { get; private init; }

    /// <summary>The Card in force.</summary>
    public Terms(DraftCard current)
        : this(Of(current.Grants), Of(current.Totals), current.OnExceed, HoldersOf(current.Subject, current.Subjects), current.Domain,
            current.Environment, current.Location, Instant(current.ValidFrom), Instant(current.ValidUntil)) { }

    /// <summary>A draft's Card, read over the Card in force it revises: one that names no holders keeps the Card's, when its first holder is the same.</summary>
    public Terms(DraftCard card, DraftCard over)
        : this(Of(card.Grants), Of(card.Totals), card.OnExceed, HoldersOver(card, over), card.Domain, card.Environment, card.Location,
            Instant(card.ValidFrom), Instant(card.ValidUntil)) { }

    /// <param name="validFrom">Already read as an instant: never read twice.</param>
    /// <param name="validUntil">Already read as an instant.</param>
    public Terms(IEnumerable<Grant> grants, IEnumerable<Total> totals, string? onExceed, IEnumerable<string> holders, string? domain,
        string? environment, string? location, string validFrom, string validUntil)
    {
        Grants = new(grants.Select(g => g with
            {
                Caps = new(g.Caps.OrderBy(CapKey, StringComparer.Ordinal)),
                Scope = new(g.Scope.Order(StringComparer.Ordinal)),
            }).OrderBy(GrantKey, StringComparer.Ordinal));
        Totals = new(totals.OrderBy(TotalKey, StringComparer.Ordinal));
        OnExceed = onExceed;
        Holders = new(holders);
        Domain = (domain ?? "").ToLowerInvariant();
        Environment = (environment ?? "").ToLowerInvariant();
        Location = location ?? "";
        ValidFrom = validFrom;
        ValidUntil = validUntil;
    }

    /// <summary>
    /// A time as KIFF writes it, as the instant it names, as the iPad's <c>Terms.instant</c>: "@" and milliseconds since 1970,
    /// so the same moment written with another offset or precision is the same term. None is "". Text that can't be read as
    /// a time is kept behind "?", a mark no instant has, so it equals only the same text, and never a time.
    /// </summary>
    public static string Instant(string? time)
    {
        if (string.IsNullOrEmpty(time)) return "";
        // KiffActivity.Parse's pattern ends at "$", which lets one trailing line break through; the iPad's formatter doesn't.
        if (time.EndsWith('\n') || KiffActivity.Parse(time) is not { } at) return "?" + time;
        var ms = Math.Round((decimal)(at.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TimeSpan.TicksPerMillisecond, MidpointRounding.AwayFromZero);
        return "@" + ((long)ms).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>These terms with their grants or totals replaced, put back in order (the iPad's <c>with</c>).</summary>
    public Terms With(IEnumerable<Grant>? grants = null, IEnumerable<Total>? totals = null) =>
        new(grants ?? Grants, totals ?? Totals, OnExceed, Holders, Domain, Environment, Location, ValidFrom, ValidUntil);

    static IEnumerable<Grant> Of(IReadOnlyList<DraftGrant>? grants) =>
        (grants ?? []).Select(g => new Grant(g.Action,
            new((g.PerActionMax ?? []).Select(c => new Cap(c.Quantity, c.Max, c.Unit, c.Scale))),
            g.Resources?.Parameter, new(g.Resources?.Values ?? []), new(g.EntityStates ?? []), new(g.NextSteps ?? [])));

    static IEnumerable<Total> Of(IReadOnlyList<DraftTotal>? totals) =>
        (totals ?? []).Select(a => new Total(a.Quantity, a.Limit, a.Window, a.Unit, a.Scale,
            // Left out, KIFF counts attempts for a count and effects for a sum.
            string.IsNullOrEmpty(a.Measure) ? (a.Quantity == "count" ? "attempts" : "effects") : a.Measure, a.Per ?? ""));

    /// <summary>Every holder, the first being the subject.</summary>
    static IReadOnlyList<string> HoldersOf(string? subject, IReadOnlyList<string>? subjects) =>
        subjects is { Count: > 0 } ? subjects : subject is null ? [] : [subject];

    static IReadOnlyList<string> HoldersOver(DraftCard card, DraftCard over)
    {
        var inForce = HoldersOf(over.Subject, over.Subjects);
        return (card.Subjects ?? []).Count == 0 && card.Subject is { } subject && inForce.Count > 0 && subject == inForce[0]
            ? inForce : HoldersOf(card.Subject, card.Subjects);
    }

    static string Number(long n) => n.ToString(CultureInfo.InvariantCulture);
    static string Number(int? n) => n?.ToString(CultureInfo.InvariantCulture) ?? "";

    static string CapKey(Cap c) => $"{c.Quantity}|{c.Unit ?? ""}|{Number(c.Max)}|{Number(c.Scale)}";

    static string GrantKey(Grant g) =>
        $"{g.Action}|{string.Join(";", g.Caps.Select(CapKey))}|{g.ScopeOf ?? ""}|{string.Join(",", g.Scope)}|{string.Join(",", g.States)}|{string.Join(",", g.NextSteps)}";

    static string TotalKey(Total t) => $"{t.Quantity}|{t.Window}|{t.Per}|{t.Measure}|{t.Unit ?? ""}|{Number(t.Limit)}|{Number(t.Scale)}";
}

/// <summary>
/// One limit an offer changes, as the iPad's <c>AllowOffer.Change</c>: on which Card, what kind (<c>per_action</c>,
/// <c>total</c>, <c>count</c> or <c>resource</c>), from what to what, and in tafs cloud's words. A <c>resource</c> change
/// names the parameter whose scope grows and the one value added.
/// </summary>
public sealed record AllowChange(string MandateId, string Kind, long? Before = null, long? After = null, string? Unit = null,
    string? Window = null, string? Parameter = null, string? Value = null, string? WordsBefore = null, string? WordsAfter = null,
    bool? Rounded = null);

/// <summary>What the offer would have let go over the last 30 days, as the iPad's <c>AllowOffer.Would</c>.</summary>
public sealed record AllowWould(long? Calls = null, string? Words = null);

/// <summary>What rounding would give, named before any tap, as the iPad's <c>AllowOffer.Round</c>. The app never rounds.</summary>
public sealed record AllowRound(long? After = null, string? Words = null);

/// <summary>
/// tafs cloud's offer, as the iPad's <c>AllowOffer</c>: the narrowest change, what it would have let go (left out when KIFF's
/// replay couldn't be read), the draft KIFF holds for it, and what rounding would give (absent when it changes nothing).
/// </summary>
public sealed record AllowOffer(IReadOnlyList<AllowChange> Changes, string Draft, AllowWould? Would = null, string? ConfirmKey = null,
    bool? SendHeld = null, AllowRound? Round = null);

/// <summary>
/// tafs cloud's answer on a held call, as the iPad's <c>AllowAnswer</c>: the offer, or why there's none (<c>none</c>, with its
/// <c>words</c>). A <c>none</c> this app doesn't know reads as any other: its words, and nothing to sign.
/// </summary>
public sealed record AllowAnswer(AllowOffer? Offer = null, string? None = null, string? Words = null);

/// <summary>tafs cloud's offer route, as the iPad's <c>TafsCloud.allowOffer</c>: <c>POST /v1/tafs/{id}/holds/{exception}/allow</c>.</summary>
public static class AllowOffers
{
    public static string Path(string taf, string exception) =>
        $"/v1/tafs/{CloudRead.PathPart(taf)}/holds/{CloudRead.PathPart(exception)}/allow";

    /// <summary><c>{}</c> for the exact figure, <c>{"round":true}</c> for the rounded one. Rounding is tafs cloud's, never the app's.</summary>
    public static byte[] Body(bool round) => Encoding.UTF8.GetBytes(round ? """{"round":true}""" : "{}");

    /// <summary>
    /// The answer, read as strictly as the iPad's decoder: a required field missing, or any field of another shape where it's
    /// there and not null, fails the answer. Fields tafs cloud adds later are no matter.
    /// </summary>
    public static AllowAnswer? Read(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || !Shape.Optional(a, "offer", IsOffer) || !Shape.Optional(a, "none", Shape.Text)
            || !Shape.Optional(a, "words", Shape.Text)) return null;
        return new(Lenient.Field(a, "offer") is { ValueKind: JsonValueKind.Object } o ? Offer(o) : null, Lenient.Text(a, "none"), Lenient.Text(a, "words"));
    }

    static bool IsOffer(JsonElement o) =>
        Shape.Required(o, "changes", v => Shape.List(v, IsChange)) && Shape.Required(o, "draft", Shape.Text)
        && Shape.Optional(o, "would", w => w.ValueKind == JsonValueKind.Object && Shape.Optional(w, "calls", Shape.Int) && Shape.Optional(w, "words", Shape.Text))
        && Shape.Optional(o, "confirm_key", Shape.Text) && Shape.Optional(o, "send_held", Shape.Bool)
        && Shape.Optional(o, "round", r => r.ValueKind == JsonValueKind.Object && Shape.Optional(r, "after", Shape.Int) && Shape.Optional(r, "words", Shape.Text));

    static bool IsChange(JsonElement c) =>
        Shape.Required(c, "mandate_id", Shape.Text) && Shape.Required(c, "kind", Shape.Text)
        && new[] { "before", "after" }.All(n => Shape.Optional(c, n, Shape.Int))
        && new[] { "unit", "window", "parameter", "value", "words_before", "words_after" }.All(n => Shape.Optional(c, n, Shape.Text))
        && Shape.Optional(c, "rounded", Shape.Bool);

    static AllowOffer Offer(JsonElement o)
    {
        static JsonElement? Object(JsonElement e, string n) => Lenient.Field(e, n) is { ValueKind: JsonValueKind.Object } x ? x : null;
        return new(
            Lenient.Field(o, "changes")!.Value.EnumerateArray().Select(c => new AllowChange(Lenient.Text(c, "mandate_id")!, Lenient.Text(c, "kind")!,
                Whole(c, "before"), Whole(c, "after"), Lenient.Text(c, "unit"), Lenient.Text(c, "window"), Lenient.Text(c, "parameter"),
                Lenient.Text(c, "value"), Lenient.Text(c, "words_before"), Lenient.Text(c, "words_after"), Flag(c, "rounded"))).ToList(),
            Lenient.Text(o, "draft")!,
            Object(o, "would") is { } w ? new AllowWould(Whole(w, "calls"), Lenient.Text(w, "words")) : null,
            Lenient.Text(o, "confirm_key"), Flag(o, "send_held"),
            Object(o, "round") is { } r ? new AllowRound(Whole(r, "after"), Lenient.Text(r, "words")) : null);
    }

    /// <summary>A whole number as Swift reads an <c>Int64</c>: 40 and 40.0 alike (<see cref="Shape.Int"/> checked it's whole).</summary>
    static long? Whole(JsonElement o, string n) =>
        Lenient.Field(o, n) is { ValueKind: JsonValueKind.Number } v ? v.TryGetInt64(out var l) ? l : (long)v.GetDouble() : null;

    static bool? Flag(JsonElement o, string n) =>
        Lenient.Field(o, n) is { ValueKind: JsonValueKind.True or JsonValueKind.False } v ? v.GetBoolean() : null;
}

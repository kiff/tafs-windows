using System.Text;
using System.Text.Json;
using Tafs.Core.Approvals;
using Tafs.Core.Taf;
using Tafs.Core.Words;

namespace Tafs.Core.Coach;

/// <summary>The step's words, every one tafs cloud's, in the person's language (the iPad's <c>CoachTools.Step</c>).</summary>
public sealed record ToolsStep(string Title, string Intro, IReadOnlyList<StepGroup> Groups, string Details, string Ask, string Give,
    string NotNow, string Row)
{
    /// <summary>
    /// What to say when Give went but nothing was connected (tafs cloud still asks for the same batch), in tafs cloud's words
    /// (<c>not_connected</c>); empty from a tafs cloud that doesn't send it, when the app's own <c>coach.tools_not_given</c> is said.
    /// </summary>
    public string NotConnected { get; init; } = "";
}

/// <summary>
/// Coach's tools as tafs cloud says KIFF has them (<c>GET /v1/setup</c>'s <c>tools</c>, the iPad's <c>TafsCloud.CoachTools</c>):
/// <c>given</c>, <c>to_give</c> or <c>taken</c> (a vendor's tool holds a name, <see cref="Taken"/>); the batch for the gateway, Coach's
/// Cards' draft with KIFF's words for them, and the pair to sign once; and the step's words. This PC builds none of the requests.
/// </summary>
public sealed record CoachTools(string State, string? Taken, StartRequest? Connect, StartRequest? Card, FirstStart? Together, ToolsStep? Step)
{
    public bool ToGive => State == "to_give";
    public bool Given => State == "given";

    /// <summary>Read from <c>GET /v1/setup</c>'s answer; none from a tafs cloud that sends no <c>tools</c>.</summary>
    public static CoachTools? Read(JsonElement setup)
    {
        if (setup.ValueKind != JsonValueKind.Object || Lenient.Field(setup, "tools") is not { ValueKind: JsonValueKind.Object } t) return null;
        StartRequest? Request(string name, string path, bool gateway)
        {
            if (Lenient.Field(t, name) is not { ValueKind: JsonValueKind.Object } r || !r.TryGetProperty("body", out var body)) return null;
            var lines = Lenient.Array(r, "lines").Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList();
            var cards = Lenient.Array(r, "cards").Where(c => c.ValueKind == JsonValueKind.Object)
                .Select(c => (Title: Lenient.Text(c, "title") ?? Lenient.Text(c, "card_title") ?? Lenient.Text(c, "label"),
                    Lines: Lenient.Array(c, "lines").Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()))
                .Where(c => c.Title is not null || c.Lines.Count > 0).Select(c => new CardWords(c.Title ?? "", c.Lines)).ToList();
            return new(name, "POST", path, gateway, Encoding.UTF8.GetBytes(body.GetRawText()), lines, Lenient.Text(r, "card_title"), cards);
        }
        // A Coach that exists, with tools and Cards missing: the pair, signed once, as a first start's. Only one
        // this PC may sign, with KIFF's words for the Cards.
        var pair = Lenient.Field(t, "requests") is { ValueKind: JsonValueKind.Array } ? FirstStart.Read(Wrap(t)) : null;
        var together = pair is { Together: true, Startable: true } ? pair : null;
        var card = Request("card", CoachStart.DraftPath, gateway: false) ?? together?.Draft;
        return new(Lenient.Text(t, "state") ?? "unknown", Lenient.Text(t, "taken"), Request("connect", "/v1/connections/batch", gateway: true),
            card, together, Lenient.Field(t, "coach_tools_step") is { ValueKind: JsonValueKind.Object } s ? ReadStep(s) : null);
    }

    /// <summary>The tools object as a first start's <c>{"start": …}</c>, so its pair reads the same way.</summary>
    static JsonElement Wrap(JsonElement tools) =>
        JsonDocument.Parse("{\"start\":" + tools.GetRawText() + "}").RootElement.Clone();

    static ToolsStep ReadStep(JsonElement s)
    {
        string T(string n) => Lenient.Text(s, n) ?? "";
        var groups = FirstStart.Read(JsonDocument.Parse("{\"start\":{\"coach_tools_step\":" + s.GetRawText() + "}}").RootElement)?.Groups ?? [];
        return new(T("title"), T("intro"), groups, T("details"), T("ask"), T("give"), T("not_now"), T("row")) { NotConnected = T("not_connected").Trim() };
    }

    /// <summary>
    /// Coach takes no typing until its tools are approved, as the iPhone's <c>composerLocked</c> and Android's: while the tools
    /// themselves are still to give (a batch to connect, alone or in the pair), the box is one locked line that opens the step.
    /// When only Cards for newer tools are missing, typing stays open.
    /// </summary>
    public bool LocksBox => ToGive && (Connect is not null || Together?.Connect is not null);

    /// <summary>tafs cloud says which tools a draft carries (<c>in_draft</c>): the step's data is there to compare with.</summary>
    public bool MarksDraft => Step?.Groups.Any(g => g.Tools.Any(t => t.InDraft is not null)) == true;

    /// <summary>
    /// Shown = signed for a later approval of Coach's Cards, as the iPhone's <c>draftSigns</c>: when tafs cloud marks which tools
    /// this draft carries, those must be exactly what the draft signs (<see cref="CardStepCheck"/>). True with no draft to sign,
    /// and from a tafs cloud that doesn't mark them, where KIFF's own words are what's shown as signed.
    /// </summary>
    public bool DraftSigns => (Card?.Body ?? Together?.Draft?.Body) is not { } draft || Step is not { } step || !MarksDraft
        || CardStepCheck.Matches(step.Groups, draft, "");

    /// <summary>
    /// The tools batch this read asks to sign (alone, or in the pair), held to the step as the first start's is, once tafs
    /// cloud marks the step's data. True with no batch, and from a tafs cloud that doesn't mark it.
    /// </summary>
    public bool BatchSigns(string mcp) => (Connect?.Body ?? Together?.Connect?.Body) is not { } batch || Step is not { } step || !MarksDraft
        || CardStepCheck.BatchMatches(step.Groups, batch, mcp);

    /// <summary>The locked box's line: the step's <c>row</c> in tafs cloud's words, else the app's "Coach's tools". Never blank.</summary>
    public string LockedLine(WordBook w) => Step?.Row.Trim() is { Length: > 0 } row ? row : w.Get("coach.tools.title");

    /// <summary>The row above the conversation, in tafs cloud's words, else the app's own: only while there's something to give.</summary>
    public string? Row(WordBook w) => ToGive ? Step?.Row is { Length: > 0 } row ? row : w.Get("coach.tools.give") : null;

    /// <summary>
    /// Whether <paramref name="read"/> asks to sign exactly what this showed: the same requests, byte for byte, and the same
    /// words for Coach's Cards. Anything else isn't what the person read.
    /// </summary>
    public bool SignsAsShown(CoachTools read) =>
        Same(Together?.Connect, read.Together?.Connect) && Same(Together?.Draft, read.Together?.Draft)
        && Same(Connect, read.Connect) && Same(Card, read.Card);

    internal static bool Same(StartRequest? a, StartRequest? b) => (a, b) switch
    {
        (null, null) => true,
        ({ } x, { } y) => x.Id == y.Id && x.Method == y.Method && x.Path == y.Path && x.Gateway == y.Gateway
            && x.Body.AsSpan().SequenceEqual(y.Body) && x.Lines.SequenceEqual(y.Lines) && x.CardTitle == y.CardTitle
            && x.Cards.Count == y.Cards.Count
            && x.Cards.Zip(y.Cards).All(c => c.First.Title == c.Second.Title && c.First.Lines.SequenceEqual(c.Second.Lines)),
        _ => false,
    };
}

/// <summary>
/// What giving Coach its tools came to: given, or why not. All empty: Windows Hello closed, nothing said. <see cref="Changed"/>:
/// tafs cloud's read at the tap asks for something other than what the step showed, so nothing was signed, and this is what
/// to show instead.
/// </summary>
public sealed record ToolsGiven(bool Given, string? Why = null, CoachTools? Changed = null);

/// <summary>
/// Giving Coach its tools on this PC (P8), as the iPad's <c>giveCoachTools</c>: tafs cloud's read again; taken says which name;
/// then, with tafs cloud's pair, the batch and Coach's Cards under one Windows Hello (this PC's passkey asserting
/// <c>kiff.requests.v1</c>), the batch first and the draft once it answered; with a draft alone, one Windows Hello over it. The
/// tafs cloud read again says whether they're given. Only what the step showed is signed: a read at the tap that asks for
/// anything else signs nothing, and comes back to be shown and asked again.
/// </summary>
public static class CoachToolsGive
{
    /// <param name="shown">Coach's tools as the step the person read showed them.</param>
    public static async Task<ToolsGiven> GiveAsync(WordBook w, StartCalls calls, CoachTools shown)
    {
        var none = new ToolsGiven(false);
        var read = await calls.ReadSetup();
        if (!calls.Current()) return none;
        if (read.Answer is not { } setup) return new(false, w.Get("coach.tools_not_read", ("why", read.Why ?? w.Get("error.offline"))));
        if (CoachTools.Read(setup) is not { } tools) return none;
        switch (tools.State)
        {
            case "given": return new(true);
            case "taken": return new(false, tools.Taken is { } name ? w.Get("coach.taken.named", ("name", name)) : w.Get("coach.taken.unnamed"));
            case "to_give": break;
            default: return new(false, w.Get("coach.tools_unknown"));
        }
        // tafs cloud rebuilt the pair since the step was drawn (more tools, other Cards): nothing the person didn't read is signed.
        if (!shown.SignsAsShown(tools)) return new(false, w.Get("coach.setup_changed_request_see_below"), tools);
        // The list the step shows must be exactly what the draft and the batch sign, before Windows Hello: else nothing is signed.
        if (!tools.DraftSigns || !tools.BatchSigns(calls.Mcp)) return new(false, w.Get("version.out_of_date_card"), tools);
        string Failed(string why) => w.Get("coach.tools_not_given", ("why", why));
        try
        {
            if (tools.Together is { Connect: { } connect, Draft: { } draft })
            {
                var (header, batch, cards) = await calls.SignTogether(connect, draft, calls.SignedAt());
                if (!calls.Current()) return none;
                var (status, body) = await calls.SendBatch(connect.Body, (header, batch));
                if (!calls.Current()) return none;
                if (status is < 200 or > 299) return new(false, Failed(CoachStart.KiffRefused(w, status, body)));
                (status, body) = await calls.SubmitDraft(draft.Body, (header, cards));
                if (!calls.Current()) return none;
                if (status is < 200 or > 299) return new(false, Failed(CoachStart.KiffRefused(w, status, body)));
            }
            else if (tools.Connect is null && tools.Card is { Cards.Count: > 0 } card)
            {
                // The Cards alone, read in KIFF's own words: one Windows Hello over the draft.
                var signature = await calls.SignDraft(card.Body, calls.SignedAt());
                if (!calls.Current()) return none;
                var (status, body) = await calls.SubmitDraft(card.Body, signature);
                if (!calls.Current()) return none;
                if (status is < 200 or > 299) return new(false, Failed(CoachStart.KiffRefused(w, status, body)));
            }
            // A batch without its pair can't go under the one approval this PC signs: said, nothing sent.
            else return new(false, w.Get("coach.tools_unknown"));
        }
        catch (OperationCanceledException) { return none; }
        catch (PasskeyRefusedException e) { return calls.Current() ? new(false, Failed(e.Message)) : none; }
        catch (HttpRequestException) { return calls.Current() ? new(false, Failed(w.Get("error.offline"))) : none; }
        var after = await calls.ReadSetup();
        if (!calls.Current()) return none;
        if (after.Answer is not { } a || CoachTools.Read(a) is not { } now) return new(false, w.Get("coach.tools_unknown"));
        if (now.Given) return new(true);
        // The batch went and KIFF took it, yet tafs cloud still asks for the very same batch: nothing was connected (KIFF counted
        // every tool as already there). Said, in tafs cloud's words when it sends them, never as if KIFF hadn't answered (as the
        // iPad's and Android's giveCoachTools).
        if (now.ToGive && tools.Together?.Connect is { } sent && (now.Together?.Connect ?? now.Connect) is { } asked && asked.Body.AsSpan().SequenceEqual(sent.Body))
            return new(false, now.Step?.NotConnected is { Length: > 0 } said ? said : w.Get("coach.tools_not_given", ("why", "")).Trim());
        return new(false, w.Get("coach.tools_unknown"));
    }
}

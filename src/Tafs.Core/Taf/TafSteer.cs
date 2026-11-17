using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>A message written to a run at work, or held for after it: its words and when.</summary>
public sealed record SteerMessage(string Text, DateTimeOffset At);

/// <summary>What a stopped run never read, with tafs cloud's words for why.</summary>
public sealed record SteerNotSent(string Text, DateTimeOffset At, string Why);

/// <summary>
/// What the thread says of messages written while the taf works, as the iPad's <c>TafsCloud.Steering</c>: the run, its
/// status, what it hasn't read yet (<c>untaken</c>), what waits to go once it's done (<c>held</c>), and what a stopped run
/// never read (<c>not_sent</c>).
/// </summary>
public sealed record Steering(string Run, string Status, IReadOnlyList<SteerMessage> Untaken, SteerMessage? Held, IReadOnlyList<SteerNotSent> NotSent)
{
    /// <summary>A paused run reads it when it carries on; any other, at its next step.</summary>
    public bool Paused => Status.Equals("PAUSED", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What writing to the run came to.</summary>
public abstract record Steered
{
    /// <summary>The run took it: it reads it at its next step (a paused run, when it carries on).</summary>
    public sealed record Taken(string Status) : Steered;
    /// <summary>The run didn't take it, and tafs cloud kept it: sent at once as the next message, or held until the taf is done.</summary>
    public sealed record Kept(bool Sent, SteerMessage? Held) : Steered;
    /// <summary>More than tafs cloud holds (409 <c>held_full</c>): tafs cloud's words; the words stay in the box.</summary>
    public sealed record Full(string Why) : Steered;
    /// <summary>Not taken: kept in the thread as not sent, as any message, with why.</summary>
    public sealed record Refused(Refusal Why) : Steered;
}

/// <summary>
/// Writing to a taf while it works (P34), as the iPad's <c>submitChat</c> and <c>TafsCloud.steer</c>: the box's words go to
/// the run at work (<c>POST /v1/tafs/{id}/steer</c> with <c>{run, text, hold: true}</c>), once. What the run doesn't take,
/// tafs cloud holds and sends once the taf is done, so the app never sends it again. A message with files goes on its own,
/// never into the run.
/// </summary>
public static class TafSteer
{
    public static string Path(string taf) => "/v1/tafs/" + CloudRead.PathPart(taf) + "/steer";

    public static byte[] Body(string run, string text) => JsonSerializer.SerializeToUtf8Bytes(new { run, text, hold = true });

    /// <summary>The run a message would go to: the thread's at work (<c>working.run</c>).</summary>
    public static string? WorkingRun(JsonElement thread) =>
        Lenient.Field(thread, "working") is { ValueKind: JsonValueKind.Object } w ? Lenient.Text(w, "run") : null;

    /// <summary>The thread's <c>steering</c>; none when it says none. A message it can't read is left out, not the others.</summary>
    public static Steering? Read(JsonElement thread)
    {
        if (Lenient.Field(thread, "steering") is not { ValueKind: JsonValueKind.Object } s
            || Lenient.Text(s, "run") is not { } run || Lenient.Text(s, "status") is not { } status) return null;
        SteerMessage? Message(JsonElement m) =>
            Lenient.Text(m, "text") is { } text && Lenient.At(m, "at") is { } at ? new(text, at) : null;
        var notSent = Lenient.Array(s, "not_sent")
            .Select(m => Message(m) is { } said && Lenient.Text(m, "why") is { } why ? new SteerNotSent(said.Text, said.At, why) : null)
            .OfType<SteerNotSent>().ToList();
        return new(run, status, Lenient.Array(s, "untaken").Select(Message).OfType<SteerMessage>().ToList(),
            Lenient.Field(s, "held") is { ValueKind: JsonValueKind.Object } h ? Message(h) : null, notSent);
    }

    /// <summary>Under a message the run hasn't read: "Refunds reads this at its next step", or "…when it carries on".</summary>
    public static string ReadsLine(WordBook w, string taf, bool paused) =>
        w.Get(paused ? "chat.steer.taf_reads_paused" : "chat.steer.taf_reads_next", ("taf", taf));

    /// <summary>tafs cloud's answer, as the iPad's <c>steer</c>: <c>status</c> is taken; otherwise kept, sent or held.</summary>
    public static Steered Answer(WordBook w, CloudRead r)
    {
        if (r.Answer is not { } a)
            return r.Status == 409 && (r.Code == "held_full" || r.Why == "held_full")
                ? new Steered.Full(r.Words ?? (r.Why is { } why && why != "held_full" ? why : w.Get("error.ours")))
                : new Steered.Refused(Refusal.Of(r, w.Get("error.offline")));
        if (Lenient.Text(a, "status") is { } status) return new Steered.Taken(status);
        var held = Lenient.Field(a, "held") is { ValueKind: JsonValueKind.Object } h && Lenient.Text(h, "text") is { } text
            ? new SteerMessage(text, Lenient.At(h, "at") ?? DateTimeOffset.UtcNow) : null;
        return new Steered.Kept(Lenient.Field(a, "sent") is { ValueKind: JsonValueKind.Object }, held);
    }

    public static async Task<Steered> SteerAsync(WordBook w, string taf, string run, string text, Func<string, byte[], Task<CloudRead>> send) =>
        Answer(w, await send(Path(taf), Body(run, text)));
}

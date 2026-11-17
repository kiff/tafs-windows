using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>One server-sent event from tafs cloud's stream: its name and its JSON data (none when it isn't JSON).</summary>
public sealed record StreamEvent(string Name, JsonElement? Data);

/// <summary>
/// Server-sent events as tafs cloud writes them (kiff/tafs <c>docs/chat.md</c>, "The answer as it comes"): lines of
/// <c>event:</c> and <c>data:</c>, an event ending at a blank line, <c>:</c> lines ignored, several data lines joined by a
/// line break.
/// </summary>
public static class ServerSentEvents
{
    public static IEnumerable<StreamEvent> Parse(IEnumerable<string> lines)
    {
        var e = new Reader();
        foreach (var line in lines)
            if (e.Add(line) is { } done) yield return done;
        if (e.Add("") is { } last) yield return last;
    }

    public static async IAsyncEnumerable<StreamEvent> ReadAsync(TextReader reader, [EnumeratorCancellation] CancellationToken cancel = default)
    {
        var e = new Reader();
        while (await reader.ReadLineAsync(cancel) is { } line)
            if (e.Add(line) is { } done) yield return done;
        if (e.Add("") is { } last) yield return last;
    }

    sealed class Reader
    {
        string name = "message";
        readonly StringBuilder data = new();
        bool any;

        public StreamEvent? Add(string line)
        {
            if (line.Length == 0)
            {
                if (!any) return null;
                var text = data.ToString();
                StreamEvent e = new(name, Json(text));
                (name, any) = ("message", false);
                data.Clear();
                return e;
            }
            if (line.StartsWith(':')) return null;
            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? "" : line[(colon + 1)..].TrimStart(' ');
            if (field == "event") (name, any) = (value, true);
            else if (field == "data")
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(value);
                any = true;
            }
            return null;
        }

        static JsonElement? Json(string text)
        {
            try { return text.Length == 0 ? null : JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException) { return null; }
        }
    }
}

/// <summary>One step of a live answer: its call, its words, and how it ended (running, done, error, not made), with why.</summary>
public sealed record LiveStep(string CallId, string? Words, string Status, string? Why = null);

/// <summary>
/// A taf's answer as it comes, as the iPad's <c>LiveAnswer</c> and Android's <c>LiveWork</c>: the chat and run it's in (for
/// Stop and steering), the owner's message as the thread names it, tafs cloud's live line, the words so far, the steps, and
/// how it ended. The model's thinking text is never kept: neither app draws it.
/// </summary>
public sealed record LiveAnswer(string? Session, string? Run, string? MessageId, string? State, string? StateWords, string Text,
    IReadOnlyList<LiveStep> Steps, bool Ended = false, string? EndStatus = null, string? EndWords = null, bool Broken = false,
    bool Taken = false)
{
    public static readonly LiveAnswer Start = new(null, null, null, null, null, "", []);

    /// <summary>The answer after one more event. An event the app doesn't know, or data it can't read, changes nothing.</summary>
    public LiveAnswer Apply(StreamEvent e)
    {
        var d = e.Data ?? default;
        string? T(string name) => d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        switch (e.Name)
        {
            case "session":
                var message = d.ValueKind == JsonValueKind.Object && d.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object
                    && m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
                return this with { Session = T("session") ?? Session, Run = T("run") ?? Run, MessageId = message ?? MessageId };
            case "state":
                return T("state") is { } state ? this with { State = state, StateWords = T("words") } : this;
            case "text":
                return T("text") is { } text ? this with { Text = Text + text } : this;
            case "activity":
                return T("call_id") is { } call && Steps.All(s => s.CallId != call)
                    ? this with { Steps = [.. Steps, new LiveStep(call, T("step"), "running")] } : this;
            case "step":
                if (T("call_id") is not { } ended) return this;
                var status = T("status") ?? "done";
                return this with { Steps = Steps.Select(s => s.CallId == ended ? s with { Status = status, Why = T("why") } : s).ToList() };
            case "taken":
                return this with { Taken = true };
            case "end":
                return this with { Ended = true, State = "done", EndStatus = T("status"), EndWords = T("words") };
            case "error":
                return this with { Ended = true, Broken = true };
            default:
                return this;
        }
    }

    /// <summary>
    /// The steps done so far, as the iPad's <c>LiveWork</c> draws them under the live line: every step but the one running,
    /// folded by <see cref="Tafs.Core.Coach.WorkLog.Rows"/> (tries of one step as one row, failures left out while it works, so
    /// what shows is what got done). A step with no words is left out: the app has nothing to say for it.
    /// </summary>
    public IReadOnlyList<Tafs.Core.Coach.WorkRow> DoneRows()
    {
        var running = Steps.Select((s, i) => (s, i)).LastOrDefault(x => x.s.Status == "running", (null!, -1)).Item2;
        var done = Steps.Where((s, i) => i != running && s.Words is { Length: > 0 })
            .Select(s => (s.Words!, Status(s.Status))).ToList();
        return Tafs.Core.Coach.WorkLog.Rows(done, running: true);
    }

    static Tafs.Core.Coach.StepStatus Status(string status) => status switch
    {
        "error" => Tafs.Core.Coach.StepStatus.Error,
        "held" => Tafs.Core.Coach.StepStatus.Held,
        "not_made" => Tafs.Core.Coach.StepStatus.NotMade,
        "running" => Tafs.Core.Coach.StepStatus.Running,
        _ => Tafs.Core.Coach.StepStatus.Done,
    };

    /// <summary>
    /// The live line, as kiff/tafs <c>docs/chat.md</c>'s: tafs cloud's <c>state</c> once it's sent ("Thinking…", the step's
    /// words or Coach's note with "…", "Working…", "Waiting for you", nothing at done); before it, the step at work, else
    /// "Thinking…".
    /// </summary>
    public string? Line(WordBook w) => State switch
    {
        "done" => null,
        "waiting_for_you" => w.Get("chat.waiting_for_you"),
        "working" => StateWords is { Length: > 0 } words ? w.Get("chat.working_on", ("step", words)) : w.Get("chat.working"),
        "thinking" => w.Get("chat.thinking"),
        _ when Ended => null,
        _ => Steps.LastOrDefault(s => s.Status == "running")?.Words is { Length: > 0 } step
            ? w.Get("chat.working_on", ("step", step)) : w.Get("chat.thinking"),
    };
}

/// <summary>Writing to a taf with its answer streamed (<c>POST /v1/tafs/{id}/messages/stream</c>), as the iPad's send.</summary>
public static class TafChatStream
{
    public static string Path(string taf) => $"/v1/tafs/{CloudRead.PathPart(taf)}/messages/stream";

    /// <summary>
    /// A message's stream opened, as the iPad's send: open, its answer to read as it comes. Not reached, or no stream route
    /// there (404, 405, an older tafs cloud or the stream's host), the plain send takes its place under the same id
    /// (<c>client_message_id</c>), so a stream that reached tafs cloud before failing is acted on once. Any other answer is a
    /// refusal, said and never sent again.
    /// </summary>
    /// <returns>The open stream; else whether the plain send took it (no refusal) or why not.</returns>
    public static async Task<(System.Net.Http.HttpResponseMessage? Open, Refusal? Refused)> OpenAsync(WordBook w, string taf, string text,
        IReadOnlyList<string>? files, string? messageId,
        Func<string, byte[], Task<(int Status, string Body, System.Net.Http.HttpResponseMessage? Open)>> stream,
        Func<string, byte[], Task<CloudRead>> send)
    {
        (int Status, string Body, System.Net.Http.HttpResponseMessage? Open) answer;
        try { answer = await stream(Path(taf), TafChatSend.Body(text, files, messageId)); }
        catch (System.Net.Http.HttpRequestException) { return (null, await TafChatSend.SendAsync(w, taf, text, send, files, messageId: messageId)); }
        if (answer.Open is null && answer.Status is 404 or 405) return (null, await TafChatSend.SendAsync(w, taf, text, send, files, messageId: messageId));
        if (answer.Open is not { } open) return (null, Refusal.Of(CloudRead.Refused(w, answer.Status, answer.Body), w.Get("error.ours")));
        return (open, null);
    }

    /// <summary>
    /// A run of the person's picked back up (the iPad's <c>resumeRun</c>): its steps so far, then the rest as it goes, until it
    /// stops (<c>GET /v1/tafs/{id}/runs/{run}/stream?after=0</c>). Only their own run; another's is a 404.
    /// </summary>
    public static string ResumePath(string taf, string run) =>
        $"/v1/tafs/{CloudRead.PathPart(taf)}/runs/{CloudRead.PathPart(run)}/stream?after=0";
}

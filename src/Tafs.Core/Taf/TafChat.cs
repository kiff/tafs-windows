using Tafs.Core.Coach;
using Tafs.Core.Words;

namespace Tafs.Core.Taf;

/// <summary>
/// A taf's chat read and drawn, as the iPad's <c>loadChat</c>, <c>chatItems</c> and <c>TafChatView.thread</c>: its thread from
/// tafs cloud (<c>GET /v1/tafs/{id}/conversation</c>), the same shape as Coach's, so <see cref="CoachThread.Read"/> reads it.
/// Writing to it, its live answer and Stop come in their own change.
/// </summary>
public static class TafChat
{
    /// <summary>The taf's thread on tafs cloud, as the iPad's <c>TafsCloud.conversation</c>.</summary>
    public static string Path(string taf) => $"/v1/tafs/{CloudRead.PathPart(taf)}/conversation";

    /// <summary>
    /// Its thread, as the iPad's <c>loadChat</c> on KIFF: tafs cloud's answer read as a thread, or "The thread couldn't be
    /// loaded." and why (the iPad's <c>chatErrors</c>): tafs cloud's words, else not reached; for an answer that doesn't
    /// decode, the iPad says the system's words, and this app its own.
    /// </summary>
    /// <param name="path">Another thread's path, read the same way: a trial's (<see cref="TafTrials.ConversationPath"/>).</param>
    public static async Task<(CoachThread? Thread, string? Error)> LoadAsync(WordBook w, string taf, Func<string, Task<CloudRead>> read,
        string? path = null)
    {
        var answer = await read(path ?? Path(taf));
        if (answer.Answer is not { } a) return (null, NotLoaded(w, answer.Why ?? w.Get("error.offline")));
        return CoachThread.Read(a) is { } thread ? (thread, null) : (null, NotLoaded(w, w.Get("error.ours")));
    }

    static string NotLoaded(WordBook w, string why) => w.Get("chat.thread_not_loaded", ("why", why));

    /// <summary>
    /// What the thread shows, in order, as the iPad's <c>chatItems</c> from its messages: each once, by time, the same message
    /// twice showing once. Held calls, what became of them, knocks and Coach's hand-offs come in their own changes.
    /// </summary>
    public static IReadOnlyList<ThreadItem> Items(CoachThread thread) => thread.Items.Where(x => x.Message is not null).ToList();

    /// <summary>
    /// A sample taf's thread, as the iPad's <c>loadChat</c> in sample mode: its greeting, "Hi, I'm Refunds. I'm on it: …",
    /// with what it's doing now, else the iPad's word for a taf with nothing set, Ready (<c>Models.swift</c>'s <c>now</c>).
    /// </summary>
    public static IReadOnlyList<ChatMessage> Sample(WordBook w, string name, string now, DateTimeOffset at) =>
        [new(at, ChatFrom.Taf, false, w.Get("sample.hello", ("name", name), ("now", now.Length > 0 ? now : w.Get("taf.state.ready"))), "sample")];

    /// <summary>
    /// Under an empty thread, as the iPad's <c>Words.Chat.emptyNamed</c>: "Ask Refunds what it did, …", or "it" with no name.
    /// </summary>
    public static string Empty(WordBook w, string? name) => w.Get("chat.empty_named", ("taf", string.IsNullOrEmpty(name) ? w.Get("chat.it_lower") : name));

    /// <summary>
    /// What a taf is doing, in the person's words, from the tool it calls, as the iPad's <c>tafActivityWords</c>: a
    /// connected tool by its title ("Asking KIFF: Create refund…"), its notes, what waits for the person, else the tool's
    /// own name in words ("Asking KIFF to run list charges…"). Every tool that reaches the company's systems goes through KIFF.
    /// </summary>
    public static string ActivityWords(WordBook w, string tool, Func<string, string?>? title = null)
    {
        if (title?.Invoke(tool) is { Length: > 0 } t) return w.Get("kiff.asking_kiff", ("title", t));
        return tool switch
        {
            "memory" => w.Get("taf.activity.reading_notes"),
            "kiff_pending" => w.Get("taf.activity.checking_waits"),
            _ => w.Get("work.asking_kiff_run", ("tool", CoachChat.ToolWords(tool))),
        };
    }

    /// <summary>
    /// A taf's finished work, as its <c>WorkSummary</c>: every step, in the taf's words, tries of a failed one folded into one row.
    /// </summary>
    public static IReadOnlyList<WorkRow> WorkRows(WordBook w, ChatWork work, Func<string, string?>? title = null) =>
        WorkLog.Rows(work.Steps.Select(s => (ActivityWords(w, s.Tool, title), s.NotMade ? StepStatus.NotMade : s.Error ? StepStatus.Error : StepStatus.Done)).ToList(),
            running: false);
}

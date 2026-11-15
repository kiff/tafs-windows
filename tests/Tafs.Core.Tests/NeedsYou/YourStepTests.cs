using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tafs.Core.NeedsYou;
using Tafs.Core.Tests.Strings;
using Tafs.Core.Words;

namespace Tafs.Core.Tests.NeedsYou;

/// <summary>
/// Needs you's "Your step", as the iPad's YourStepTests: KIFF's pinned vector for kiff.handoff.v1, what tafs cloud sends, the
/// vouched link, and answering it (read, confirm, sign, post) with each way it can go.
/// </summary>
[Part("approvals")]
public class YourStepTests
{
    static readonly WordBook En = WordBook.Load(SharedFiles.Strings, "en");

    const string VectorSeed = "kiff.handoff.v1 test vector";
    const string VectorSpkiDigest = "85c559569edf864d4b3186426f3541f8bbf8c424d96de17f707b919b2c593970";
    const string VectorSha256 = "1b9f7f923489891b736af1fbc494c9348690494d4ae4901844aeb9a0b6df3b6f";
    const string VectorSignature = "MEUCIQDJLYymcVMzMDhv+kRXK1BS2DhZvJROyBCjJLzzTy5NDgIgXe7a2OeeShmHHI5HazLS+/BzMjLVBYpjX7JT0Uy67RA=";

    /// <summary>The message is KIFF's byte for byte, and KIFF's signature over it verifies with the vector's key; never for "skipped".</summary>
    [Fact]
    public void The_answer_message_matches_KIFF_s_vector()
    {
        var message = YourSteps.Message("t_vector", "ho_0123456789abcdef01234567", "done", 1, "2026-11-11T17:00:00Z");
        Assert.Equal(string.Join((char)10, "kiff.handoff.v1", "t_vector", "ho_0123456789abcdef01234567", "done", "1", "2026-11-11T17:00:00Z"),
            Encoding.UTF8.GetString(message));
        Assert.Equal(VectorSha256, Convert.ToHexString(SHA256.HashData(message)).ToLowerInvariant());

        using var key = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = SHA256.HashData(Encoding.UTF8.GetBytes(VectorSeed)) });
        Assert.Equal(VectorSpkiDigest, Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant());
        var theirs = Convert.FromBase64String(VectorSignature);
        Assert.True(key.VerifyData(message, theirs, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        Assert.True(key.VerifyData(message, key.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
        var skipped = YourSteps.Message("t_vector", "ho_0123456789abcdef01234567", "skipped", 1, "2026-11-11T17:00:00Z");
        Assert.False(key.VerifyData(skipped, theirs, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
    }

    static string Step(string handoff = "ho_1", string? link = "https://app.qonto.com/transfers/tr_9", string? host = "app.qonto.com",
        string? open = "Open in Qonto")
    {
        var parts = new List<string> { $"\"handoff\":\"{handoff}\"", "\"run\":\"run_1\"", "\"taf\":{\"id\":\"depl_1\",\"name\":\"Rae\"}",
            "\"what\":\"Approve the transfer of €1,200 to Acme\"", "\"why\":\"Only you may approve a transfer.\"" };
        if (link is not null) parts.Add($"\"link\":\"{link}\"");
        if (host is not null) parts.Add($"\"link_host\":\"{host}\"");
        parts.Add("\"since\":\"2026-11-11T17:00:00Z\"");
        parts.Add("\"expires\":\"2026-11-18T17:00:00Z\"");
        parts.Add("\"words\":{\"title\":\"Your step\",\"done\":\"Done\",\"skip\":\"Skip it\"" + (open is null ? "" : $",\"open\":\"{open}\"")
            + ",\"line\":\"Your step: Approve the transfer of €1,200 to Acme\"}");
        return "{" + string.Join(",", parts) + "}";
    }

    static IReadOnlyList<YourStep>? Home(params string[] steps) =>
        YourSteps.Read(JsonDocument.Parse("{\"tafs\":[],\"your_steps\":[" + string.Join(",", steps) + "]}").RootElement);

    /// <summary>A button only for a link tafs cloud vouched for: https, its host given apart and the link's own, with the button's words.</summary>
    [Fact]
    public void The_vendor_button_shows_only_for_a_vouched_https_link()
    {
        var vouched = Home(Step())![0];
        Assert.Equal(("https://app.qonto.com/transfers/tr_9", "app.qonto.com", "Open in Qonto"),
            YourSteps.OpenLink(vouched) is { } l ? (l.Url.AbsoluteUri, l.Host, l.Words) : default);
        Assert.Equal(("depl_1", "Rae"), (vouched.TafId, vouched.TafName));
        Assert.Equal("Your step: Approve the transfer of €1,200 to Acme", vouched.Said.Line);
        Assert.Null(YourSteps.OpenLink(Home(Step(link: null, host: null, open: null))![0]));
        Assert.Null(YourSteps.OpenLink(Home(Step(open: null))![0]));
        Assert.Null(YourSteps.OpenLink(Home(Step(host: null))![0]));
        Assert.Null(YourSteps.OpenLink(Home(Step(link: "http://app.qonto.com/transfers/tr_9"))![0]));
        Assert.Null(YourSteps.OpenLink(Home(Step(link: "https://app.qonto.com.evil.example/login"))![0]));
        Assert.Null(YourSteps.OpenLink(Home(Step(link: "javascript:alert(1)"))![0]));
    }

    /// <summary>Only the hand-off is required; none is none; a step without its hand-off makes the list unreadable.</summary>
    [Fact]
    public void Home_s_steps_read_as_the_iPad_s()
    {
        Assert.Equal(new YourStep("ho_2", Words: new StepWords()), Home("""{"handoff":"ho_2","taf":{"name":"no id"},"what":3,"words":"x"}""")![0]);
        Assert.Empty(YourSteps.Read(JsonDocument.Parse("""{"tafs":[]}""").RootElement)!);
        Assert.Empty(YourSteps.Read(JsonDocument.Parse("""{"your_steps":null}""").RootElement)!);
        Assert.Null(Home("""{"what":"no hand-off"}"""));
        Assert.Null(YourSteps.Read(JsonDocument.Parse("""{"your_steps":{}}""").RootElement));
    }

    /// <summary>Answered steps Home still lists stay hidden; one Home no longer lists is forgotten.</summary>
    [Fact]
    public void Steps_answered_here_stay_hidden_until_Home_drops_them()
    {
        var answered = new HashSet<string> { "ho_1", "ho_gone" };
        var shown = YourSteps.Shown(Home(Step("ho_1"), Step("ho_2"))!, answered);
        Assert.Equal(["ho_2"], shown.Select(s => s.Handoff));
        Assert.Equal(["ho_1"], answered);
    }

    [Fact]
    public void Who_answers_the_routes_the_body_and_the_prompt()
    {
        Assert.True(YourSteps.CanAnswer(kiff: true, mayAnswerHolds: true, "t_1", hasKey: true));
        Assert.False(YourSteps.CanAnswer(kiff: true, mayAnswerHolds: false, "t_1", hasKey: true));
        Assert.False(YourSteps.CanAnswer(kiff: true, mayAnswerHolds: true, null, hasKey: true));
        Assert.False(YourSteps.CanAnswer(kiff: true, mayAnswerHolds: true, "t_1", hasKey: false));
        Assert.False(YourSteps.CanAnswer(kiff: false, mayAnswerHolds: true, "t_1", hasKey: true));
        Assert.Equal("/v1/me/handoffs/ho%2F1", YourSteps.HandoffPath("ho/1"));
        Assert.Equal("/v1/me/handoffs/ho%2F1/answer", YourSteps.AnswerPath("ho/1"));
        Assert.Equal("""{"answer":"skipped","signature":{"signed_at":"2026-11-11T17:00:00.000Z","value":"MEU="}}""",
            Encoding.UTF8.GetString(YourSteps.AnswerBody("skipped", "2026-11-11T17:00:00.000Z", "MEU=")));
        var step = Home(Step())![0];
        Assert.Equal("Done · Approve the transfer of €1,200 to Acme", YourSteps.Prompt(step, done: true));
        Assert.Equal("Skip it · Approve the transfer of €1,200 to Acme", YourSteps.Prompt(step, done: false));
        Assert.Equal("Approve", YourSteps.Prompt(new YourStep("h", What: "Approve"), done: true));
        Assert.Equal(new Handoff("ho_1", 1, ""), YourSteps.ReadHandoff(JsonDocument.Parse("""{"id":"ho_1","version":"2"}""").RootElement));
        Assert.Null(YourSteps.ReadHandoff(JsonDocument.Parse("""{"version":2}""").RootElement));
    }

    /// <summary>KIFF's error, else its reason, in the app's words; else KIFF didn't take it, with the app's line, never the code.</summary>
    [Fact]
    public void Why_KIFF_didn_t_take_it()
    {
        Assert.Equal("Your role on this account doesn't answer held calls. Someone who approves on it does.",
            YourSteps.RefusedWords(En, 403, """{"error":"role_forbidden"}"""));
        Assert.Equal("You asked for this, so someone else on the account approves it. You can still refuse it.",
            YourSteps.RefusedWords(En, 403, """{"error":"forbidden","reason":"self_review"}"""));
        Assert.Equal("KIFF didn't take the answer: Your role on this account doesn't allow this.",
            YourSteps.RefusedWords(En, 403, """{"error":"handoff_expired"}"""));
        Assert.Equal("KIFF didn't take the answer: Signed out of KIFF. Sign in again.", YourSteps.RefusedWords(En, 401, "{}"));
        Assert.Equal("KIFF didn't take the answer: It isn't working on our side right now. Try again later.", YourSteps.RefusedWords(En, 503, "oops"));
        Assert.Equal("KIFF didn't take the answer: It didn't reach the server. Try again once you're online.", YourSteps.RefusedWords(En, 0, ""));
    }

    sealed class Kiff
    {
        public (int, string) Handoff = (200, """{"id":"ho_1","version":2,"status":"waiting"}""");
        public (int, string) Answer = (200, """{}""");
        public StepConfirmed Confirmed = StepConfirmed.Yes;
        public bool Current = true;
        public bool SignOutAfterConfirm;
        public readonly List<string> Calls = [];
        public string? Prompt;
        public byte[]? Signed;
        public string? Posted;

        public Task<(StepAnswered, string?)> Answer_(YourStep step, bool done) => YourSteps.AnswerAsync(En, step, done, "t_1",
            path => { Calls.Add("GET " + path); return Task.FromResult(Handoff); },
            prompt => { Prompt = prompt; Calls.Add("confirm"); if (SignOutAfterConfirm) Current = false; return Task.FromResult(Confirmed); },
            message => { Signed = message; Calls.Add("sign"); return Task.FromResult("MEU="); },
            (path, body) => { Posted = Encoding.UTF8.GetString(body); Calls.Add("POST " + path); return Task.FromResult(Answer); },
            () => "2026-11-11T17:00:00.000Z", () => Current);
    }

    /// <summary>Read, confirmed in tafs cloud's words, signed over kiff.handoff.v1 at KIFF's version, then posted.</summary>
    [Fact]
    public async Task An_answer_is_read_confirmed_signed_and_posted()
    {
        var kiff = new Kiff();
        Assert.Equal((StepAnswered.Answered, null), await kiff.Answer_(Home(Step())![0], done: true));
        Assert.Equal(["GET /v1/me/handoffs/ho_1", "confirm", "sign", "POST /v1/me/handoffs/ho_1/answer"], kiff.Calls);
        Assert.Equal("Done · Approve the transfer of €1,200 to Acme", kiff.Prompt);
        Assert.Equal(YourSteps.Message("t_1", "ho_1", "done", 2, "2026-11-11T17:00:00.000Z"), kiff.Signed);
        Assert.Equal("""{"answer":"done","signature":{"signed_at":"2026-11-11T17:00:00.000Z","value":"MEU="}}""", kiff.Posted);
    }

    /// <summary>Closed, or not confirmed, nothing is signed or sent and the step stays.</summary>
    [Fact]
    public async Task Not_confirmed_nothing_is_signed()
    {
        var kiff = new Kiff { Confirmed = StepConfirmed.Cancelled };
        Assert.Equal((StepAnswered.NotAnswered, "Cancelled, so nothing was approved. The request is kept: approve again when you're ready."),
            await kiff.Answer_(Home(Step())![0], done: false));
        kiff = new Kiff { Confirmed = StepConfirmed.Failed };
        Assert.Equal((StepAnswered.NotAnswered, "Not answered: it wasn't confirmed."), await kiff.Answer_(Home(Step())![0], done: true));
        Assert.DoesNotContain("sign", kiff.Calls);
        Assert.Null(kiff.Posted);
    }

    /// <summary>A hand-off KIFF no longer waits on, or a 409, has ended; a failed read or post says why; a sign-out stops it.</summary>
    [Fact]
    public async Task Each_other_way_it_goes()
    {
        var step = Home(Step())![0];
        var ended = (StepAnswered.Ended, (string?)"This step was already answered or has ended.");
        Assert.Equal(ended, await new Kiff { Handoff = (200, """{"id":"ho_1","status":"done"}""") }.Answer_(step, true));
        Assert.Equal(ended, await new Kiff { Answer = (409, """{"error":"handoff_answered"}""") }.Answer_(step, true));
        Assert.Equal((StepAnswered.NotAnswered, "Your answer didn't go through. KIFF didn't take the answer: Signed out of KIFF. Sign in again."),
            await new Kiff { Handoff = (401, "") }.Answer_(step, true));
        Assert.Equal((StepAnswered.NotAnswered, "Your answer didn't go through. KIFF didn't take the answer: Your role on this account doesn't allow this."),
            await new Kiff { Answer = (403, """{"error":"handoff_expired"}""") }.Answer_(step, true));
        var signedOut = new Kiff { SignOutAfterConfirm = true };
        Assert.Equal((StepAnswered.Stopped, null), await signedOut.Answer_(step, true));
        Assert.Null(signedOut.Posted);
    }
}

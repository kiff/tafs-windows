using System.Net.Http;
using System.Net.Http.Headers;
using Tafs.Core.SignIn;

namespace Tafs.App;

/// <summary>
/// The app's one way to KIFF and tafs cloud: a JSON POST, or a read as the session, answered as status and body. Bodies
/// are never logged: a 200 can hold a token. Signed routes add their headers on top of this in their own change.
/// </summary>
static class KiffHttp
{
    static readonly HttpClient Client = new() { BaseAddress = DeviceSignIn.Api, Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>For a card's answer, which can take tafs cloud a while: 70 seconds, as the iPad's <c>sendData</c>.</summary>
    static readonly HttpClient Patient = new() { Timeout = TimeSpan.FromSeconds(70) };

    /// <summary>Storage, for a file's bytes: its own client, so no session ever goes with them.</summary>
    static readonly HttpClient Storage = new() { Timeout = TimeSpan.FromSeconds(120) };

    /// <summary>
    /// A file's bytes, straight to storage where tafs cloud's declare said (https only, as <c>Attachments.Declared</c> takes
    /// it), with its headers as given and no session. True once storage took them.
    /// </summary>
    public static async Task<bool> UploadAsync(Tafs.Core.Coach.FileUpload upload, byte[] bytes, CancellationToken cancel = default)
    {
        if (upload.Url.Scheme != Uri.UriSchemeHttps) return false;
        using var request = new HttpRequestMessage(new HttpMethod(upload.Method), upload.Url) { Content = new ByteArrayContent(bytes) };
        foreach (var (name, value) in upload.Headers)
            if (!request.Headers.TryAddWithoutValidation(name, value)) request.Content.Headers.TryAddWithoutValidation(name, value);
        try
        {
            using var answer = await Storage.SendAsync(request, cancel);
            return answer.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return false; }
    }

    /// <summary>A sent file's bytes, from the one-minute link tafs cloud gave (https only), with no session; none when it fails.</summary>
    public static async Task<byte[]?> DownloadAsync(Uri link, long most, CancellationToken cancel = default)
    {
        if (link.Scheme != Uri.UriSchemeHttps) return null;
        try
        {
            using var answer = await Storage.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!answer.IsSuccessStatusCode || answer.Content.Headers.ContentLength > most) return null;
            // Read up to the most a file may be, never more: a wrong link never fills memory.
            await using var body = await answer.Content.ReadAsStreamAsync(cancel);
            using var kept = new MemoryStream();
            var buffer = new byte[81920];
            int n;
            while ((n = await body.ReadAsync(buffer, cancel)) > 0)
            {
                if (kept.Length + n > most) return null;
                kept.Write(buffer, 0, n);
            }
            return kept.ToArray();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return null; }
    }

    /// <summary>For an answer that streams while a taf works: no overall timeout, ended by its own end or a cancel.</summary>
    static readonly HttpClient Streaming = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    static KiffHttp()
    {
        Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("tafs-windows", "0.1"));
        Patient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("tafs-windows", "0.1"));
        Streaming.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("tafs-windows", "0.1"));
    }

    /// <summary>
    /// A JSON POST as the session to tafs cloud whose answer streams as server-sent events (a message to a taf): the same
    /// hosts as <see cref="PostAsync(Uri, byte[], string, string, CancellationToken, (string Header, string Value)?, (string Header, string Value)?)"/>.
    /// A 2xx gives the open answer to read events from, which the caller disposes; anything else, its status and body.
    /// </summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't a host the session's token may go to.</exception>
    public static async Task<(int Status, string Body, HttpResponseMessage? Open)> PostStreamAsync(Uri url, byte[] body, string bearer, string language,
        CancellationToken cancel = default)
    {
        if (!Tafs.Core.Kiff.KeptSessions.MaySendTo(url, DeviceSignIn.Api)) throw new HttpRequestException("not a host the session goes to");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var answer = await Streaming.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (answer.IsSuccessStatusCode) return ((int)answer.StatusCode, "", answer);
        using (answer) return ((int)answer.StatusCode, await answer.Content.ReadAsStringAsync(cancel), null);
    }

    /// <summary>A run's stream opened again as the session (tafs cloud's stream host only), held open as it goes.</summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't a host the session's token may go to.</exception>
    public static async Task<(int Status, string Body, HttpResponseMessage? Open)> GetStreamAsync(Uri url, string bearer, string language,
        CancellationToken cancel = default)
    {
        if (!Tafs.Core.Kiff.KeptSessions.MaySendTo(url, DeviceSignIn.Api)) throw new HttpRequestException("not a host the session goes to");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var answer = await Streaming.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (answer.IsSuccessStatusCode) return ((int)answer.StatusCode, "", answer);
        using (answer) return ((int)answer.StatusCode, await answer.Content.ReadAsStringAsync(cancel), null);
    }

    /// <exception cref="HttpRequestException">KIFF wasn't reached.</exception>
    /// <param name="bearer">The session's token, for a call as that session.</param>
    public static async Task<(int Status, string Body)> PostAsync(string path, byte[] body, CancellationToken cancel = default, string? bearer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await SendAsync(request, cancel);
    }

    /// <summary>A read of one of KIFF's public routes (the terms version in force), with no session: nothing to sign in as yet.</summary>
    /// <exception cref="HttpRequestException">KIFF wasn't reached.</exception>
    public static async Task<(int Status, string Body)> PublicGetAsync(string path, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await SendAsync(request, cancel);
    }

    /// <summary>
    /// A read as the session, from KIFF or another host it signs in to (tafs cloud), in the app's language: tafs cloud
    /// words its lines in it.
    /// </summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't a host the session's token may go to.</exception>
    /// <param name="deviceSignature">planque's signed read of the grants, forwarded by tafs cloud with a taf's Setup.</param>
    public static async Task<(int Status, string Body)> GetAsync(Uri url, string bearer, string language, CancellationToken cancel = default,
        string? deviceSignature = null)
    {
        if (!Tafs.Core.Kiff.KeptSessions.MaySendTo(url, DeviceSignIn.Api)) throw new HttpRequestException("not a host the session goes to");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        if (deviceSignature is not null) request.Headers.Add("Planque-Device-Signature", deviceSignature);
        return await SendAsync(request, cancel);
    }

    /// <summary>A JSON PUT as the session to tafs cloud or KIFF (a setting changed), the same hosts as a read.</summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't a host the session's token may go to.</exception>
    public static async Task<(int Status, string Body)> PutAsync(Uri url, byte[] body, string bearer, string language, CancellationToken cancel = default,
        (string Header, string Value)? signature = null)
    {
        if (!Tafs.Core.Kiff.KeptSessions.MaySendTo(url, DeviceSignIn.Api)) throw new HttpRequestException("not a host the session goes to");
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // A signed setup call's header (KIFF-Device-Signature), when it is one.
        if (signature is { } signed) request.Headers.Add(signed.Header, signed.Value);
        return await SendAsync(request, cancel, Patient);
    }

    /// <summary>
    /// A signed change as the session to KIFF (People &amp; groups' POST, PATCH, PUT or DELETE): a JSON body when there is one, with
    /// none for an empty one, so what's sent is what was signed. The same hosts as a read.
    /// </summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't a host the session's token may go to.</exception>
    public static async Task<(int Status, string Body)> SignedChangeAsync(HttpMethod method, Uri url, byte[] body, string bearer, string language,
        (string Header, string Value) signature, CancellationToken cancel = default)
    {
        if (!Tafs.Core.Kiff.KeptSessions.MaySendTo(url, DeviceSignIn.Api)) throw new HttpRequestException("not a host the session goes to");
        using var request = new HttpRequestMessage(method, url);
        if (body.Length > 0)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add(signature.Header, signature.Value);
        return await SendAsync(request, cancel, Patient);
    }

    /// <summary>A DELETE as the session to tafs cloud or KIFF (a halt released), in the app's language, the same hosts as a read.</summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't a host the session's token may go to.</exception>
    public static async Task<(int Status, string Body)> DeleteAsync(Uri url, string bearer, string language, CancellationToken cancel = default,
        (string Header, string Value)? signature = null)
    {
        if (!Tafs.Core.Kiff.KeptSessions.MaySendTo(url, DeviceSignIn.Api)) throw new HttpRequestException("not a host the session goes to");
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        // A removal's signature (KIFF-Passkey-Assertion), when it's signed.
        if (signature is { } signed) request.Headers.Add(signed.Header, signed.Value);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await SendAsync(request, cancel, Patient);
    }

    /// <summary>
    /// A read from planque as this device (the iPad's <c>PlanqueClient</c>), in the app's language: signed with
    /// <c>Planque-Device-Signature</c>, with no token. Only planque's exact host.
    /// </summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't planque.</exception>
    public static Task<(int Status, string Body)> PlanqueGetAsync(Uri url, string deviceSignature, string language, CancellationToken cancel = default) =>
        PlanqueSendAsync(HttpMethod.Get, url, deviceSignature, language, cancel);

    /// <summary>A bodiless planque call as this device (a read, or a Chrome unpaired), signed as <see cref="PlanqueGetAsync"/>; only planque's exact host.</summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't planque.</exception>
    public static async Task<(int Status, string Body)> PlanqueSendAsync(HttpMethod method, Uri url, string deviceSignature, string language, CancellationToken cancel = default)
    {
        if (!Tafs.Core.Kiff.PlanqueProof.IsPlanque(url)) throw new HttpRequestException("not planque");
        using var request = new HttpRequestMessage(method, url);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        request.Headers.Add("Planque-Device-Signature", deviceSignature);
        return await SendAsync(request, cancel);
    }

    /// <summary>
    /// A JSON POST as the session to tafs cloud (a card's answer) or KIFF, in the app's language, as a read is; with
    /// <paramref name="signature"/>, a signed call's header (<c>KIFF-Device-Signature</c> or <c>KIFF-Passkey-Assertion</c>).
    /// </summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't a host the session's token may go to.</exception>
    public static async Task<(int Status, string Body)> PostAsync(Uri url, byte[] body, string bearer, string language, CancellationToken cancel = default,
        (string Header, string Value)? signature = null, (string Header, string Value)? extra = null)
    {
        if (!Tafs.Core.Kiff.KeptSessions.MaySendTo(url, DeviceSignIn.Api)) throw new HttpRequestException("not a host the session goes to");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (signature is { } signed) request.Headers.Add(signed.Header, signed.Value);
        // planque's read proof beside KIFF's signature, as the iPad's approvalReport sends both.
        if (extra is { } more) request.Headers.Add(more.Header, more.Value);
        return await SendAsync(request, cancel, Patient);
    }

    /// <summary>
    /// Coach's tools batch to KIFF's gateway as the session, as the iPad's <c>connectBatch</c>: its bytes as tafs cloud wrote
    /// them, signed with <paramref name="signature"/>. Only the gateway's exact host gets the token this way.
    /// </summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't the gateway.</exception>
    public static async Task<(int Status, string Body)> GatewayPostAsync(Uri url, byte[] body, string bearer, string language,
        (string Header, string Value) signature, CancellationToken cancel = default)
    {
        if (!Tafs.Core.Coach.CoachStart.IsGateway(url)) throw new HttpRequestException("not KIFF's gateway");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add(signature.Header, signature.Value);
        return await SendAsync(request, cancel, Patient);
    }

    /// <summary>A JSON POST to planque as this device (its enrolment), signed with <c>Planque-Device-Signature</c>, with no token.</summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't planque.</exception>
    /// <param name="vendorKey">An API-key grant's key, in planque's own header only (never in what's signed or logged).</param>
    public static async Task<(int Status, string Body)> PlanquePostAsync(Uri url, byte[] body, string deviceSignature, string language,
        CancellationToken cancel = default, string? vendorKey = null)
    {
        if (!Tafs.Core.Kiff.PlanqueProof.IsPlanque(url)) throw new HttpRequestException("not planque");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        request.Headers.Add("Planque-Device-Signature", deviceSignature);
        if (vendorKey is not null) request.Headers.Add(Tafs.Core.Connections.KeySheet.KeyHeader, vendorKey);
        return await SendAsync(request, cancel);
    }

    /// <summary>A DELETE to planque as this device (erasing the account's connections), signed with <c>Planque-Device-Signature</c>.</summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't planque.</exception>
    public static async Task<(int Status, string Body)> PlanqueDeleteAsync(Uri url, string deviceSignature, string language, CancellationToken cancel = default)
    {
        if (!Tafs.Core.Kiff.PlanqueProof.IsPlanque(url)) throw new HttpRequestException("not planque");
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        request.Headers.Add("Planque-Device-Signature", deviceSignature);
        return await SendAsync(request, cancel);
    }

    /// <summary>A read as the session, its answer's bytes as they came (an export of receipts, kept exactly as issued).</summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't a host the session's token may go to.</exception>
    public static async Task<(int Status, byte[] Body)> GetBytesAsync(Uri url, string bearer, string language, CancellationToken cancel = default)
    {
        if (!Tafs.Core.Kiff.KeptSessions.MaySendTo(url, DeviceSignIn.Api)) throw new HttpRequestException("not a host the session goes to");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        try
        {
            using var answer = await Client.SendAsync(request, cancel);
            return ((int)answer.StatusCode, await answer.Content.ReadAsByteArrayAsync(cancel));
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new HttpRequestException("KIFF didn't answer in time");
        }
    }

    static async Task<(int Status, string Body)> SendAsync(HttpRequestMessage request, CancellationToken cancel, HttpClient? client = null)
    {
        try
        {
            using var answer = await (client ?? Client).SendAsync(request, cancel);
            return ((int)answer.StatusCode, await answer.Content.ReadAsStringAsync(cancel));
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new HttpRequestException("KIFF didn't answer in time");
        }
    }
}

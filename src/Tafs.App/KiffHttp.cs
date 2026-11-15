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

    static KiffHttp()
    {
        Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("tafs-windows", "0.1"));
        Patient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("tafs-windows", "0.1"));
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

    /// <summary>
    /// A read from planque as this device (the iPad's <c>PlanqueClient</c>), in the app's language: signed with
    /// <c>Planque-Device-Signature</c>, with no token. Only planque's exact host.
    /// </summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't planque.</exception>
    public static async Task<(int Status, string Body)> PlanqueGetAsync(Uri url, string deviceSignature, string language, CancellationToken cancel = default)
    {
        if (!Tafs.Core.Kiff.PlanqueProof.IsPlanque(url)) throw new HttpRequestException("not planque");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
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

    /// <summary>A JSON POST to planque as this device (its enrolment), signed with <c>Planque-Device-Signature</c>, with no token.</summary>
    /// <exception cref="HttpRequestException">It wasn't reached, or isn't planque.</exception>
    public static async Task<(int Status, string Body)> PlanquePostAsync(Uri url, byte[] body, string deviceSignature, string language,
        CancellationToken cancel = default)
    {
        if (!Tafs.Core.Kiff.PlanqueProof.IsPlanque(url)) throw new HttpRequestException("not planque");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));
        request.Headers.Add("Planque-Device-Signature", deviceSignature);
        return await SendAsync(request, cancel);
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

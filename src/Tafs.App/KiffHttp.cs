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

    static KiffHttp() => Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("tafs-windows", "0.1"));

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

    static async Task<(int Status, string Body)> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        try
        {
            using var answer = await Client.SendAsync(request, cancel);
            return ((int)answer.StatusCode, await answer.Content.ReadAsStringAsync(cancel));
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new HttpRequestException("KIFF didn't answer in time");
        }
    }
}

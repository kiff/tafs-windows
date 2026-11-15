using System.Net.Http;
using System.Net.Http.Headers;
using Tafs.Core.SignIn;

namespace Tafs.App;

/// <summary>
/// The app's one way to KIFF: a JSON POST, answered as status and body. Bodies are never logged: a 200
/// can hold a token. Signed routes add their headers on top of this in their own change.
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

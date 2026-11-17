using System.Net;
using System.Net.Http;
using Microsoft.UI.Xaml;
using Tafs.Core.Home;

namespace Tafs.App;

/// <summary>
/// "A new version of tafs is out" (docs/app.md, A new version): GitHub's latest published release, read at start and once a
/// day, told in one line at the sidebar's foot that opens its page. Tell only: nothing is downloaded or run. A failed read
/// (offline, refused, rate-limited, not a release) shows nothing and says nothing; a line already shown stays.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// No cookies and no redirects, so the request goes to GitHub's API only and carries only the app's User-Agent.
    /// </summary>
    static readonly HttpClient Releases = new(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    NewVersion? newVersion;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? newVersionTimer;

    /// <summary>
    /// Asks now and once a day, for a window that's the person's own. A screenshot, the samples and a Home from a file never
    /// read GitHub: they show the line only when told (<c>--new-version v1.0.2</c>).
    /// </summary>
    void StartNewVersion(bool reads, string? shown)
    {
        if (NewVersions.Of(shown) is not null) ShowNewVersion(new NewVersion(shown!, NewVersions.PageOf(shown!, null)));
        if (!reads) return;
        _ = CheckNewVersionAsync();
        newVersionTimer = DispatcherQueue.CreateTimer();
        newVersionTimer.Interval = NewVersions.Every;
        newVersionTimer.IsRepeating = true;
        newVersionTimer.Tick += (_, _) => _ = CheckNewVersionAsync();
        newVersionTimer.Start();
    }

    async Task CheckNewVersionAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, NewVersions.Latest);
            request.Headers.UserAgent.ParseAdd(NewVersions.UserAgent(typeof(App).Assembly.GetName().Version));
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Releases.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.OK) return;
            if (NewVersions.Read(await response.Content.ReadAsStringAsync(), typeof(App).Assembly.GetName().Version) is { } found)
                ShowNewVersion(found);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException) { }
    }

    void ShowNewVersion(NewVersion found)
    {
        newVersion = found;
        NewVersionText.Text = App.Words.Get("update.available");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(NewVersionLine, NewVersionText.Text);
        NewVersionLine.Visibility = Visibility.Visible;
    }

    void OnNewVersion(object sender, RoutedEventArgs e)
    {
        if (newVersion is { } found) _ = Windows.System.Launcher.LaunchUriAsync(found.Page);
    }
}

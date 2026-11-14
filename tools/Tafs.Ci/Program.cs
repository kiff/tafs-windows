using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Tafs.Core.Ci;

// Small CI helper, run the same way on a laptop and on a runner:
//   Tafs.Ci sync    fetch words and contracts at the pin into shared/
//   Tafs.Ci drift   fail when the pin is more than 7 days or 20 commits behind main
var root = FindRoot();
var pin = SharedPin.Parse(File.ReadAllText(Path.Combine(root, "shared-pin.json")));

try
{
    switch (args.FirstOrDefault())
    {
        case "sync": return Sync();
        case "drift": return await Drift();
        default:
            Console.Error.WriteLine("usage: Tafs.Ci sync | drift");
            return 2;
    }
}
catch (Exception e) when (e is InvalidOperationException or HttpRequestException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

int Sync()
{
    var dest = Path.Combine(root, "shared");
    var stamp = Path.Combine(dest, ".pin");
    if (File.Exists(stamp) && File.ReadAllText(stamp).Trim() == pin.Commit)
    {
        Console.WriteLine($"shared/ is already at {pin.Commit[..7]}");
        return 0;
    }

    var tmp = Path.Combine(Path.GetTempPath(), "tafs-sync-" + Guid.NewGuid().ToString("N"));
    try
    {
        Run("git", $"-c gc.auto=0 -c maintenance.auto=false clone --quiet --filter=blob:none --no-checkout --sparse https://github.com/{pin.Repo}.git \"{tmp}\"");
        Run("git", $"-C \"{tmp}\" sparse-checkout set {string.Join(' ', pin.Paths)}");
        Run("git", $"-C \"{tmp}\" checkout --quiet {pin.Commit}");
        if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        foreach (var p in pin.Paths) Copy(Path.Combine(tmp, p), Path.Combine(dest, p));
        File.WriteAllText(stamp, pin.Commit);
        Console.WriteLine($"shared/ now holds {string.Join(", ", pin.Paths)} at {pin.Commit[..7]}");
        return 0;
    }
    finally
    {
        // Best effort: git's object files are read-only on Windows, and a leftover temp folder is harmless.
        try
        {
            if (Directory.Exists(tmp))
            {
                foreach (var f in Directory.EnumerateFiles(tmp, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(tmp, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

async Task<int> Drift()
{
    using var http = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("tafs-windows-ci");
    http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (!string.IsNullOrEmpty(token)) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    async Task<JsonElement> Get(string url)
    {
        using var res = await http.GetAsync(url);
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
    }

    DateTimeOffset Date(JsonElement commit) => commit.GetProperty("commit").GetProperty("committer").GetProperty("date").GetDateTimeOffset();

    var pinned = await Get($"repos/{pin.Repo}/commits/{pin.Commit}");
    var head = await Get($"repos/{pin.Repo}/commits/main");
    var compare = await Get($"repos/{pin.Repo}/compare/{pin.Commit}...main");
    var result = PinDrift.Evaluate(Date(pinned), Date(head), compare.GetProperty("ahead_by").GetInt32());
    Console.WriteLine(result.Message);
    return result.Ok ? 0 : 1;
}

static void Run(string file, string arguments)
{
    using var p = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false })
        ?? throw new InvalidOperationException($"could not start {file}");
    p.WaitForExit();
    if (p.ExitCode != 0) throw new InvalidOperationException($"{file} {arguments} failed ({p.ExitCode})");
}

static void Copy(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
    foreach (var d in Directory.EnumerateDirectories(from)) Copy(d, Path.Combine(to, Path.GetFileName(d)));
}

static string FindRoot()
{
    for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "TafsWindows.sln"))) return d.FullName;
    throw new InvalidOperationException("run Tafs.Ci inside the repository");
}

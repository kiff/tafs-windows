namespace Tafs.Core.Tests.Ci;

static class RepoFiles
{
    public static string Root
    {
        get
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "TafsWindows.sln"))) return d.FullName;
            throw new InvalidOperationException("repository root not found");
        }
    }

    public static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));
}

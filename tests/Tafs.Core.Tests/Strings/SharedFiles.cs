using Tafs.Core.Tests.Ci;

namespace Tafs.Core.Tests.Strings;

/// <summary>The words and contracts fetched at the pin into shared/ (dotnet run --project tools/Tafs.Ci -- sync).</summary>
static class SharedFiles
{
    public static string Strings => Path.Combine(RepoFiles.Root, "shared", "app", "strings");
    public static string Contract(string name) => Path.Combine(RepoFiles.Root, "shared", "contracts", name);
}

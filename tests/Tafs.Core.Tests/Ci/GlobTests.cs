using Tafs.Core.Ci;

namespace Tafs.Core.Tests.Ci;

[Part("ci")]
public class GlobTests
{
    [Theory]
    [InlineData("src/*/Pairing/**", "src/Tafs.Core/Pairing/Qr.cs", true)]
    [InlineData("src/*/Pairing/**", "src/Tafs.App/Pairing/Deep/View.xaml", true)]
    [InlineData("src/*/Pairing/**", "src/Tafs.Core/Approvals/Sign.cs", false)]
    [InlineData("src/*/Pairing/**", "src/a/b/Pairing/x.cs", false)]
    [InlineData("**/*.csproj", "src/Tafs.Core/Tafs.Core.csproj", true)]
    [InlineData("**/*.csproj", "Tafs.csproj", true)]
    [InlineData("*.sln", "TafsWindows.sln", true)]
    [InlineData("*.sln", "src/TafsWindows.sln", false)]
    [InlineData(".github/**", ".github/workflows/ci.yml", true)]
    [InlineData("tools/**", "tools\\Tafs.Ci\\Program.cs", true)]
    public void Matches(string pattern, string path, bool expected) =>
        Assert.Equal(expected, Glob.IsMatch(pattern, path));
}

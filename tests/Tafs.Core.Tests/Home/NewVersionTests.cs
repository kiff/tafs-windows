using Tafs.Core.Home;

namespace Tafs.Core.Tests.Home;

[Part("home")]
public class NewVersionTests
{
    static readonly Version Running = new(1, 0, 0, 763);

    static string Release(string tag, string draft = "false", string prerelease = "false", string? url = null) =>
        $$"""{"tag_name":"{{tag}}","draft":{{draft}},"prerelease":{{prerelease}},"html_url":"{{url ?? $"https://github.com/kiff/tafs-windows/releases/tag/{tag}"}}"}""";

    [Fact]
    public void A_newer_release_is_told_with_its_own_page()
    {
        var told = NewVersions.Read(Release("v1.0.2"), Running);
        Assert.Equal("v1.0.2", told!.Tag);
        Assert.Equal(new Uri("https://github.com/kiff/tafs-windows/releases/tag/v1.0.2"), told.Page);
    }

    [Theory]
    [InlineData("v1.10.0", 1, 9, 3)]
    [InlineData("v2.0.0", 1, 99, 99)]
    [InlineData("v1.0.0", 0, 1, 757)]
    public void Versions_compare_by_number(string tag, int major, int minor, int build) =>
        Assert.NotNull(NewVersions.Read(Release(tag), new Version(major, minor, build, 0)));

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.9.9")]
    public void The_same_or_an_older_release_is_not(string tag) => Assert.Null(NewVersions.Read(Release(tag), Running));

    /// <summary>A release build's fourth number is CI's run, not part of the version: 1.0.0.763 is 1.0.0.</summary>
    [Fact]
    public void A_release_builds_run_number_isnt_a_newer_version() =>
        Assert.Null(NewVersions.Read(Release("v1.0.0"), new Version(1, 0, 0, 900)));

    [Theory]
    [InlineData("true", "false")]
    [InlineData("false", "true")]
    public void Drafts_and_prereleases_never_count(string draft, string prerelease) =>
        Assert.Null(NewVersions.Read(Release("v1.0.2", draft, prerelease), Running));

    [Fact]
    public void A_release_that_doesnt_say_it_isnt_a_draft_doesnt_count() =>
        Assert.Null(NewVersions.Read("""{"tag_name":"v1.0.2","prerelease":false}""", Running));

    [Theory]
    [InlineData("""{"message":"Not Found","documentation_url":"https://docs.github.com/rest"}""")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("<html>rate limited</html>")]
    [InlineData("")]
    [InlineData(null)]
    public void What_isnt_a_release_is_nothing(string? json) => Assert.Null(NewVersions.Read(json, Running));

    [Theory]
    [InlineData("v1.0.2-rc1")]
    [InlineData("nightly")]
    [InlineData("v1.0")]
    [InlineData("v1.0.2.1")]
    [InlineData("1.0.2")]
    [InlineData("v١.٠.٢")]
    public void A_tag_that_isnt_v_and_three_numbers_isnt_a_version(string tag)
    {
        Assert.Null(NewVersions.Of(tag));
        Assert.Null(NewVersions.Read(Release(tag), Running));
    }

    [Theory]
    [InlineData("https://evil.example/releases/tag/v1.0.2")]
    [InlineData("https://github.com/someone/tafs-windows/releases/tag/v1.0.2")]
    [InlineData("https://github.com/kiff/tafs-windows/releases/tag/v1.0.3")]
    [InlineData("http://github.com/kiff/tafs-windows/releases/tag/v1.0.2")]
    public void Only_our_own_release_page_opens_else_the_releases_page(string url) =>
        Assert.Equal(NewVersions.Releases, NewVersions.Read(Release("v1.0.2", url: url), Running)!.Page);

    [Fact]
    public void It_asks_GitHub_for_the_latest_published_release_only() =>
        Assert.Equal("https://api.github.com/repos/kiff/tafs-windows/releases/latest", NewVersions.Latest.ToString());

    [Fact]
    public void The_User_Agent_is_the_app_and_its_version_only()
    {
        Assert.Equal("tafs-windows/1.0.0", NewVersions.UserAgent(Running));
        Assert.Equal("tafs-windows/0.0.0", NewVersions.UserAgent(null));
    }

    [Fact]
    public void Without_its_own_version_the_app_tells_nothing() => Assert.Null(NewVersions.Read(Release("v1.0.2"), null));
}

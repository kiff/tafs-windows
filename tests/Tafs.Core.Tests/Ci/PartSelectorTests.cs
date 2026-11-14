using System.Reflection;
using Tafs.Core.Ci;

namespace Tafs.Core.Tests.Ci;

[Part("ci")]
public class PartSelectorTests
{
    static readonly PartsConfig Config = PartsConfig.Parse(RepoFiles.Read("ci/parts.json"));

    static string[] Select(params string[] files) => PartSelector.Select(Config, files).ToArray();

    [Fact]
    public void The_nine_parts_of_the_spec_exist() =>
        Assert.Equal(
            new[] { "pairing", "approvals", "coach", "record", "connections", "calendar", "account", "strings", "contracts" }.Order(),
            Config.Parts.Keys.Where(k => k != "ci").Order());

    [Fact]
    public void Strings_and_contracts_always_run() =>
        Assert.Equal(new[] { "strings", "contracts" }.Order(), Select("docs/ci.md").Order());

    [Fact]
    public void A_change_runs_only_the_part_it_touches_plus_the_always_parts()
    {
        var parts = Select("src/Tafs.Core/Approvals/Digest.cs", "tests/Tafs.Core.Tests/Approvals/DigestTests.cs");
        Assert.Equal(new[] { "approvals", "strings", "contracts" }.Order(), parts.Order());
    }

    [Fact]
    public void Two_parts_touched_run_both() =>
        Assert.Equal(new[] { "pairing", "record", "strings", "contracts" }.Order(),
            Select("src/Tafs.App/Pairing/QrView.xaml", "src/Tafs.Core/Record/Taf.cs").Order());

    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("src/Tafs.Core/Tafs.Core.csproj")]
    [InlineData(".github/workflows/ci.yml")]
    [InlineData("src/Tafs.Core/Brand-new-folder/X.cs")]
    public void Build_wide_or_unowned_code_runs_everything(string file) =>
        Assert.Equal(Config.Parts.Count, Select(file).Length);

    [Fact]
    public void Bumping_the_pin_runs_only_the_always_parts() =>
        Assert.Equal(new[] { "strings", "contracts" }.Order(), Select("shared-pin.json").Order());

    [Fact]
    public void Every_part_named_on_a_test_is_declared()
    {
        var used = typeof(PartSelectorTests).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<PartAttribute>().Concat(
                t.GetMethods().SelectMany(m => m.GetCustomAttributes<PartAttribute>())))
            .Select(a => a.Part).Distinct();
        Assert.All(used, part => Assert.Contains(part, Config.Parts.Keys));
    }

    [Fact]
    public void An_always_part_must_exist() =>
        Assert.Throws<InvalidDataException>(() =>
            PartsConfig.Parse("""{"always":["nope"],"parts":{"a":["x/**"]}}"""));
}

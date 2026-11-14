using Tafs.Core.Ci;

namespace Tafs.Core.Tests.Ci;

[Part("ci")]
public class SharedPinTests
{
    const string Sha = "0472dec59d4fee8d05a563ebcc9ce821dccc2255";

    [Fact]
    public void The_committed_pin_is_valid_and_asks_for_words_and_contracts()
    {
        var pin = SharedPin.Parse(RepoFiles.Read("shared-pin.json"));
        Assert.Contains("app/strings", pin.Paths);
        Assert.Contains("contracts", pin.Paths);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("v1.0")]
    [InlineData("0472dec")]
    [InlineData("0472DEC59D4FEE8D05A563EBCC9CE821DCC2255A")]
    public void A_pin_must_be_a_full_lowercase_commit_sha(string commit) =>
        Assert.Throws<InvalidDataException>(() =>
            SharedPin.Parse($$"""{"repo":"kiff/tafs","commit":"{{commit}}","paths":["contracts"]}"""));

    [Theory]
    [InlineData("/etc")]
    [InlineData("../x")]
    [InlineData("")]
    public void Paths_stay_inside_the_shared_repository(string path) =>
        Assert.Throws<InvalidDataException>(() =>
            SharedPin.Parse($$"""{"repo":"kiff/tafs","commit":"{{Sha}}","paths":["{{path}}"]}"""));

    [Fact]
    public void A_pin_needs_at_least_one_path() =>
        Assert.Throws<InvalidDataException>(() =>
            SharedPin.Parse($$"""{"repo":"kiff/tafs","commit":"{{Sha}}","paths":[]}"""));
}

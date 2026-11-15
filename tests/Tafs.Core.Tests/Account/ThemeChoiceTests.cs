using Tafs.Core.Account;

namespace Tafs.Core.Tests.Account;

[Part("account")]
public class ThemeChoiceTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "tafs-theme-" + Guid.NewGuid().ToString("N"));
    string File => Path.Combine(folder, "theme");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Theory]
    [InlineData(ThemeChoice.Light)]
    [InlineData(ThemeChoice.Dark)]
    [InlineData(ThemeChoice.System)]
    public void A_choice_is_kept_across_launches(ThemeChoice theme)
    {
        ThemeChoices.Write(File, theme);
        Assert.Equal(theme, ThemeChoices.Read(File));
        Assert.Equal(ThemeChoices.Word(theme), System.IO.File.ReadAllText(File));
        Assert.False(System.IO.File.Exists(File + ".new"));
    }

    [Fact]
    public void With_none_kept_or_a_word_it_doesn_t_know_it_s_the_system_s()
    {
        Assert.Equal(ThemeChoice.System, ThemeChoices.Read(File));
        Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(File, "sepia");
        Assert.Equal(ThemeChoice.System, ThemeChoices.Read(File));
        System.IO.File.WriteAllText(File, " dark\n");
        Assert.Equal(ThemeChoice.Dark, ThemeChoices.Read(File));
    }

    [Fact]
    public void A_later_choice_replaces_the_one_before()
    {
        ThemeChoices.Write(File, ThemeChoice.Dark);
        ThemeChoices.Write(File, ThemeChoice.Light);
        Assert.Equal(ThemeChoice.Light, ThemeChoices.Read(File));
    }

    [Theory]
    [InlineData("dark", false, ThemeChoice.Dark)]
    [InlineData("light", true, ThemeChoice.Light)]
    [InlineData(null, false, ThemeChoice.Dark)]
    [InlineData(null, true, ThemeChoice.System)]
    [InlineData("sepia", false, ThemeChoice.Dark)]
    public void At_launch_the_option_wins_and_a_screenshot_never_reads_what_was_kept(string? option, bool screenshot, ThemeChoice expected) =>
        Assert.Equal(expected, ThemeChoices.AtLaunch(option, screenshot, () => ThemeChoice.Dark));
}

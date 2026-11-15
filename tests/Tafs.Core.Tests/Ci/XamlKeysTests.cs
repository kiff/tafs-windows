using Tafs.Core.Ci;

namespace Tafs.Core.Tests.Ci;

[Part("ci")]
public class XamlKeysTests
{
    const string Head = """<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">""";

    [Fact]
    public void A_key_twice_in_one_theme_dictionary_is_named()
    {
        var xaml = Head + """
            <ResourceDictionary.ThemeDictionaries>
              <ResourceDictionary x:Key="Light"><SolidColorBrush x:Key="TafsCoach" Color="#2A64F5" /><SolidColorBrush x:Key="TafsCoach" Color="#2A64F5" /></ResourceDictionary>
              <ResourceDictionary x:Key="Dark"><SolidColorBrush x:Key="TafsCoach" Color="#7FA2FF" /></ResourceDictionary>
            </ResourceDictionary.ThemeDictionaries>
            <Style x:Key="TafsCard" TargetType="Border" /><Style x:Key="TafsCard" TargetType="Border" />
            </ResourceDictionary>
            """;
        Assert.Equal([("the file's", "TafsCard"), ("Light", "TafsCoach")], XamlKeys.Duplicates(xaml));
    }

    [Fact]
    public void The_same_key_in_light_and_dark_is_how_themes_work() =>
        Assert.Empty(XamlKeys.Duplicates(Head + """
            <ResourceDictionary.ThemeDictionaries>
              <ResourceDictionary x:Key="Light"><SolidColorBrush x:Key="TafsInk" Color="#000000" /></ResourceDictionary>
              <ResourceDictionary x:Key="Dark"><SolidColorBrush x:Key="TafsInk" Color="#FFFFFF" /></ResourceDictionary>
            </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """));

    [Fact]
    public void The_app_s_theme_gives_no_key_twice() =>
        Assert.Empty(XamlKeys.Duplicates(RepoFiles.Read("src/Tafs.App/Theme.xaml")));
}

using System.Globalization;
using Microsoft.UI.Xaml;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>
/// tafs for Windows. It draws what tafs cloud sends; this first shell has no network yet.
/// Options: --lang &lt;code&gt; to speak a shipped language, --theme light|dark, and --screenshot &lt;file.png&gt;
/// to save the window and quit.
/// </summary>
public partial class App : Application
{
    MainWindow? window;

    public App() => InitializeComponent();

    /// <summary>The words in the person's language, read from the pinned files beside the app.</summary>
    public static WordBook Words { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var options = Environment.GetCommandLineArgs();
        string? Option(string name)
        {
            var i = Array.IndexOf(options, name);
            return i >= 0 && i + 1 < options.Length ? options[i + 1] : null;
        }

        var strings = Path.Combine(AppContext.BaseDirectory, "Strings");
        var shipped = WordBook.Shipped(strings);
        var wanted = Option("--lang") is { } lang
            ? new[] { lang }
            : Windows.System.UserProfile.GlobalizationPreferences.Languages.Append(CultureInfo.CurrentUICulture.Name);
        Words = WordBook.Load(strings, AppLanguage.Pick(wanted, shipped));

        window = new MainWindow(Option("--screenshot"), Option("--theme"));
        window.Activate();
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Account;

namespace Tafs.App;

/// <summary>The AI-data consent's screen, as the iPad's <c>AIConsentView</c>: its title and lines, Allow (for this version) and Not now.</summary>
static class ConsentDialog
{
    /// <summary>Allow answered; Not now, or closed, not.</summary>
    public static async Task<bool> AskAsync(XamlRoot root, AiConsent ai)
    {
        var lines = new StackPanel { Spacing = 10 };
        foreach (var line in ai.Words.Lines) lines.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        var dialog = new ContentDialog
        {
            XamlRoot = root, Title = ai.Words.Title, Content = new ScrollViewer { Content = lines, MaxHeight = 420 },
            PrimaryButtonText = ai.Words.Allow, CloseButtonText = ai.Words.NotNow.Length > 0 ? ai.Words.NotNow : App.Words.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(dialog, "ai-consent");
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

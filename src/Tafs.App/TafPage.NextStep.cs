using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Tafs.App;

public sealed partial class TafPage
{
    /// <summary>A line's next step, as the iPad's <c>NextStepRow</c>: off while it goes, one at a time.</summary>
    async void OnNextStep(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TafLineView { Next: { } next } line } button || opening?.Next is not { } steps) return;
        button.IsEnabled = false;
        try { await steps.Do(next, line.Fallback); }
        finally { button.IsEnabled = true; }
    }
}

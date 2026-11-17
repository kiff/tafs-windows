using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// An app event's filters before this PC signs its subscription (P4), as the iPad's <c>AppEventApprovalSheet</c>: the stop's
/// label and words, "Listen only for events matching these filters.", every filter tafs cloud wrote, in mono and in full; then
/// Approve with Windows Hello, or Cancel. Why it didn't go stays in the sheet, in plain words.
/// </summary>
public sealed partial class TafPage
{
    async Task ApproveEventAsync(KnobItem item, int version, Func<int, KnobItem, Task<KnobEventResult>> approve)
    {
        if (item.Approval is not { } approval) return;
        var w = App.Words;
        var body = new StackPanel { Spacing = 14, MaxWidth = 520 };
        var label = Words(item.Label, 20, "TafsInkText");
        label.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        body.Children.Add(label);
        if (item.Words.Length > 0) body.Children.Add(Words(item.Words, 15, "TafsSoftText"));
        body.Children.Add(Words(w.Get("knob.listen_only_events_matching_filters"), 14, "TafsInkText"));
        var filters = new TextBlock
        {
            Text = KnobEvent.Filters(approval), FontSize = 13, FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Style = Look("TafsInkText"),
        };
        AutomationProperties.SetAutomationId(filters, "event.filters");
        body.Children.Add(filters);
        var why = Words("", 13, "TafsNoText");
        why.Visibility = Visibility.Collapsed;
        body.Children.Add(why);

        var approveWords = w.Get("approval.approve_with", ("biometry", Tafs.Core.Coach.CoachScreen.Biometry));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = w.Get("knob.approve_event"),
            Content = new ScrollViewer { Content = body, MaxHeight = 560 }, PrimaryButtonText = approveWords,
            CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
        };
        KnobEventResult? approved = null;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            (dialog.PrimaryButtonText, dialog.IsPrimaryButtonEnabled, dialog.IsSecondaryButtonEnabled, why.Visibility) =
                (w.Get("approval.approving"), false, false, Visibility.Collapsed);
            try
            {
                var result = await approve(version, item);
                if (result.Knob is { } read) knob = read;
                if (result.Approved) { approved = result; dialog.Hide(); }
                else if (result.Why is { } said) (why.Text, why.Visibility) = (said, Visibility.Visible);
            }
            finally
            {
                (dialog.PrimaryButtonText, dialog.IsPrimaryButtonEnabled) = (approveWords, true);
                deferral.Complete();
            }
        };
        // Cancel stays out of reach while it's signing, as the iPad's.
        dialog.Closing += (_, args) => { if (!dialog.IsPrimaryButtonEnabled && approved is null) args.Cancel = true; };
        await dialog.ShowAsync();
        ShowSetup();
        await LoadKnobAsync();
    }
}

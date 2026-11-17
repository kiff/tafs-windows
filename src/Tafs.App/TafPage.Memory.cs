using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Tafs.Core.Taf;

namespace Tafs.App;

/// <summary>
/// What a tafs cloud taf remembers, every note, as the iPad's MemoryPanel opened from See all: each note's words, its name and
/// time, and Forget, which asks first (it can't be undone); then what the notes are for. Forgotten, it goes at once; not, it
/// comes back with why.
/// </summary>
public sealed partial class TafPage
{
    IReadOnlyList<MemoryNote>? allNotes;
    string? memoryWhy;

    async Task LoadMemoryAsync()
    {
        if (opening is not { CloudId: { } id, Read: { } read } mine) return;
        var answer = await read(TafMemory.Path(id));
        if (opening != mine) return;
        if (answer.Answer is { } a && TafMemory.Read(a) is { } notes) (allNotes, memoryWhy) = (notes, null);
        else memoryWhy = answer.Why ?? App.Words.Get("kiff.kiff_did_not_answer_expected");
        if (tab == TafTab.Setup && setupScreen == SetupScreen.Memory) ShowSetup();
    }

    void ShowMemory()
    {
        var w = App.Words;
        SetupScreenHead("taf.memory.title");
        var panel = new StackPanel { Spacing = 12 };
        if (allNotes is { } notes)
        {
            if (notes.Count == 0) panel.Children.Add(Words(w.Get("taf.memory.empty"), 14, "TafsSoftText"));
            foreach (var note in notes)
            {
                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var lines = new StackPanel { Spacing = 3 };
                lines.Children.Add(Words(note.Content, 15, "TafsInkText"));
                lines.Children.Add(Words(TafSetup.NoteLine(note, TimeZoneInfo.Local), 12, "TafsSoftText"));
                row.Children.Add(lines);
                var forget = new Button { Content = w.Get("taf.memory.forget"), Style = Look("TafsQuietButton"), VerticalAlignment = VerticalAlignment.Top };
                AutomationProperties.SetName(forget, w.Get("taf.memory.forget_label", ("note", note.Content)));
                Grid.SetColumn(forget, 1);
                forget.Click += async (_, _) => await ForgetCloudAsync(note);
                row.Children.Add(forget);
                panel.Children.Add(row);
            }
            panel.Children.Add(Words(w.Get("taf.memory.explains"), 12, "TafsSoftText"));
        }
        else if (memoryWhy is null) panel.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left });
        if (memoryWhy is { } why) panel.Children.Add(Words(why, 13, "TafsNoText"));
        SetupPane.Children.Add(Panel(panel));
    }

    /// <summary>Forget asks first, as the iPad's; then it goes at once, and comes back with why if tafs cloud didn't forget it.</summary>
    async Task ForgetCloudAsync(MemoryNote note)
    {
        if (opening is not { CloudId: { } id, Change: { } change } mine || allNotes is not { } before) return;
        var w = App.Words;
        var ask = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = w.Get("taf.memory.forget_title"), Content = w.Get("taf.memory.forget_body", ("note", note.Content)),
            PrimaryButtonText = w.Get("taf.memory.forget"), CloseButtonText = w.Get("common.cancel"), DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ActualTheme,
        };
        try { if (await ask.ShowAsync() != ContentDialogResult.Primary) return; }
        catch (System.Runtime.InteropServices.COMException) { return; }
        (allNotes, memoryWhy) = (before.Where(n => n.Id != note.Id).ToList(), null);
        ShowSetup();
        var answer = await change(HttpMethod.Delete, TafMemory.NotePath(id, note.Id), null);
        if (opening != mine) return;
        if (TafMemory.Forgotten(w, answer) is { } why) (allNotes, memoryWhy) = (before, why);
        // The summary on Setup counts again, once tafs cloud has it.
        _ = LoadSetupAsync();
        if (tab == TafTab.Setup && setupScreen == SetupScreen.Memory) ShowSetup();
    }
}

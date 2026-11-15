using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Tafs.Core.NeedsYou;
using Tafs.Core.Words;

namespace Tafs.App;

/// <summary>One held call on its card. Its countdown and its footer change while it's open, every second.</summary>
public sealed class HeldCallView : INotifyPropertyChanged
{
    readonly WordBook words;

    public HeldCallView(WordBook words, HeldCall call, DateTimeOffset now)
    {
        this.words = words;
        Call = call;
        lines = NeedsYouScreen.Card(words, call, now);
        var rgb = TafColors.Of(call.TafColor);
        Avatar = new SolidColorBrush(ColorHelper.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        var gate = NeedsYouScreen.Gate(words);
        (GateLead, GateAdd) = (gate.Lead, gate.Add);
        DetailsHeading = words.Get("common.details");
        DetailsText = string.Join(Environment.NewLine, call.Details);
    }

    /// <summary>What it's on, one line an argument, under Details; empty for a call KIFF kept no arguments for.</summary>
    public string DetailsHeading { get; }
    public string DetailsText { get; }

    public HeldCall Call { get; }
    public Brush Avatar { get; }
    public string GateLead { get; }
    public string GateAdd { get; }

    HeldCallLines lines;
    public HeldCallLines Lines => lines;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Counts down: "9:37 left", then "expired", when the answers give way to the expired line.</summary>
    public void Tick(DateTimeOffset now)
    {
        var next = NeedsYouScreen.Card(words, Call, now);
        if (next == lines) return;
        lines = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lines)));
    }

    public static Visibility Seen(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Needs you. The held calls come from KIFF in their own change: until then, none, or the iPad's samples.</summary>
public sealed partial class NeedsYouPage : Page
{
    /// <summary>Add this Windows PC, from a card's answers.</summary>
    public static event Action? AddThisDeviceChosen;

    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    IReadOnlyList<HeldCallView> views = [];

    public NeedsYouPage()
    {
        InitializeComponent();
        clock.Tick += (_, _) => { foreach (var view in views) view.Tick(DateTimeOffset.UtcNow); };
        Unloaded += (_, _) => clock.Stop();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        Heading.Text = NeedsYouScreen.Title(w);
        (EmptyLead.Text, EmptyHint.Text) = NeedsYouScreen.Empty(w);
        var calls = e.Parameter as IReadOnlyList<HeldCall> ?? [];
        var now = DateTimeOffset.UtcNow;
        views = calls.Select(c => new HeldCallView(w, c, now)).ToList();
        Calls.ItemsSource = views;
        Empty.Visibility = views.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (views.Count > 0) clock.Start();
    }

    void OnAddThisDevice(object sender, RoutedEventArgs e) => AddThisDeviceChosen?.Invoke();
}

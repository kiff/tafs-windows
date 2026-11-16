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

    public HeldCallView(WordBook words, HeldCall call, DateTimeOffset now, bool mayAnswerHolds = true, bool managesCards = true)
    {
        this.words = words;
        Call = call;
        lines = NeedsYouScreen.Card(words, call, now);
        var rgb = TafColors.Of(call.TafColor);
        Avatar = new SolidColorBrush(ColorHelper.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        var gate = NeedsYouScreen.Gate(words);
        (GateLead, GateAdd) = (gate.Lead, gate.Add);
        // The gate only while this PC can't approve: a view-only session, as the iPad's ApprovalGate.
        var answers = NeedsYouScreen.Answers(words, call, mayAnswerHolds, managesCards, App.Sessions.Kept()?.Mode);
        (AnswerLine, GateShown) = (answers.Line, answers.Gate ? Visibility.Visible : Visibility.Collapsed);
        AvatarShown = call.Avatar ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>Where the answers go, as the iPad's: who answers, or the line over a Card total; the gate only where ApprovalGate would be.</summary>
    public string? AnswerLine { get; }
    public Visibility GateShown { get; }

    /// <summary>A taf's and Coach's calls have an avatar; an unknown agent's has none.</summary>
    public Visibility AvatarShown { get; }

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

/// <summary>Needs you: KIFF's held calls, or the iPad's samples for a screenshot.</summary>
public sealed partial class NeedsYouPage : Page
{
    /// <summary>Add this Windows PC, from a card's answers.</summary>
    public static event Action? AddThisDeviceChosen;

    /// <summary>F5: Needs you read again, the Windows form of the iPad's pull-to-refresh.</summary>
    public static event Action? RefreshChosen;

    /// <summary>KIFF's <c>holds.answer</c> and <c>cards.manage</c> for the person, from <c>GET /v1/me</c>; both allowed until read.</summary>
    internal static (bool MayAnswerHolds, bool ManagesCards) Capabilities { get; set; } = (true, true);

    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    IReadOnlyList<HeldCallView> views = [];

    public NeedsYouPage()
    {
        InitializeComponent();
        var refresh = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.F5 };
        refresh.Invoked += (_, e) => { e.Handled = true; RefreshChosen?.Invoke(); };
        KeyboardAccelerators.Add(refresh);
        clock.Tick += (_, _) => { foreach (var view in views) view.Tick(DateTimeOffset.UtcNow); };
        Unloaded += (_, _) => clock.Stop();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var w = App.Words;
        Heading.Text = NeedsYouScreen.Title(w);
        (EmptyLead.Text, EmptyHint.Text) = NeedsYouScreen.Empty(w);
        Show(e.Parameter as IReadOnlyList<HeldCall> ?? []);
    }

    /// <summary>The held calls, each on its card, or that nothing waits: drawn again with each read of KIFF's.</summary>
    internal void Show(IReadOnlyList<HeldCall> calls)
    {
        var w = App.Words;
        var now = DateTimeOffset.UtcNow;
        views = calls.Select(c => new HeldCallView(w, c, now, Capabilities.MayAnswerHolds, Capabilities.ManagesCards)).ToList();
        Calls.ItemsSource = views;
        Empty.Visibility = views.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (views.Count > 0) clock.Start();
        else clock.Stop();
    }

    void OnAddThisDevice(object sender, RoutedEventArgs e) => AddThisDeviceChosen?.Invoke();

    /// <summary>Brings a hold's card into view, once it's laid out: the one a notification named.</summary>
    internal bool ShowHold(string hold)
    {
        var index = views.ToList().FindIndex(v => v.Call.KiffId == hold);
        if (index < 0) return false;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            (Calls.ContainerFromIndex(index) as FrameworkElement)?.StartBringIntoView());
        return true;
    }
}

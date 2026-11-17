using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace Tafs.App;

/// <summary>
/// A line that opens and folds what's under it, as the iPad's disclosure: a button that tells Narrator whether it's open
/// (the Expand/Collapse pattern), so its state is heard, not only seen in the chevron.
/// </summary>
sealed partial class FoldButton : Button
{
    bool open;

    /// <summary>Opened or folded, by a click or by Narrator's expand and collapse.</summary>
    public event Action<bool>? Toggled;

    public FoldButton() => Click += (_, _) => Set(!open);

    public bool IsOpen => open;

    void Set(bool value)
    {
        if (open == value) return;
        open = value;
        Toggled?.Invoke(open);
        if (FrameworkElementAutomationPeer.FromElement(this) is FoldPeer peer)
            peer.RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                open ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded,
                open ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FoldPeer(this);

    sealed partial class FoldPeer(FoldButton owner) : ButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => owner.open ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public void Expand() => owner.Set(true);

        public void Collapse() => owner.Set(false);

        protected override object GetPatternCore(PatternInterface pattern) =>
            pattern == PatternInterface.ExpandCollapse ? this : base.GetPatternCore(pattern);
    }
}

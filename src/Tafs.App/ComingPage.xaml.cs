using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Tafs.App;

public sealed partial class ComingPage : Page
{
    public ComingPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e) => Heading.Text = e.Parameter as string ?? "";
}

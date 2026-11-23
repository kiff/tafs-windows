using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Tafs.App;

/// <summary>
/// The monospaced face, as the iPad's <c>.monospaced</c> design: Cascadia Mono, else Consolas (Theme.xaml's <c>TafsMono</c>).
/// In a language whose letters join (Arabic, <see cref="Tafs.Core.Words.Locale.JoinedScript"/>), the plain face instead,
/// since a fixed-width face breaks its letters apart, as the iPad's <c>ScaledFont.design</c> and Android's
/// <c>JOINED_SCRIPTS</c> (<see cref="App"/>'s <c>FollowScript</c> sets it before anything draws).
/// </summary>
public static class Monospace
{
    public static FontFamily Face => (FontFamily)Application.Current.Resources["TafsMono"];
}

using Microsoft.UI.Xaml;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    internal bool IsPlaybackVisualReady =>
        PlayerEmptyState.Visibility == Visibility.Collapsed &&
        PlayerOpeningIndicator.Visibility == Visibility.Collapsed;
}

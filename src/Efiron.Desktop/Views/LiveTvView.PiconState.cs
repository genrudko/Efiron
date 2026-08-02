using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView
{
    private void PiconImage_ImageOpened(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            SetPiconFallbackVisibility(image, Visibility.Collapsed);
        }
    }

    private void PiconImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image image)
        {
            SetPiconFallbackVisibility(image, Visibility.Visible);
        }
    }

    private static void SetPiconFallbackVisibility(
        Image image,
        Visibility visibility)
    {
        if (image.Parent is not Grid host)
        {
            return;
        }

        foreach (var child in host.Children)
        {
            if (child is TextBlock fallback)
            {
                fallback.Visibility = visibility;
                break;
            }
        }
    }
}

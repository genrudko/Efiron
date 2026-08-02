using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView
{
    internal PersistentScrollbarPixelEvidence
        GetPersistentScrollbarPixelEvidence(UIElement relativeTo)
    {
        ArgumentNullException.ThrowIfNull(relativeTo);
        EnsurePersistentVerticalScrollBar();
        UpdatePersistentVerticalScrollBar();

        if (_persistentVerticalScrollRail is null ||
            _persistentVerticalScrollThumb is null)
        {
            return PersistentScrollbarPixelEvidence.Empty;
        }

        var railOrigin = _persistentVerticalScrollRail
            .TransformToVisual(relativeTo)
            .TransformPoint(new Point(0, 0));
        var thumbOrigin = _persistentVerticalScrollThumb
            .TransformToVisual(relativeTo)
            .TransformPoint(new Point(0, 0));
        return new PersistentScrollbarPixelEvidence(
            IsVisible: _persistentVerticalScrollRail.Visibility == Visibility.Visible,
            ActualTheme: ProgrammeRoot.ActualTheme.ToString(),
            RailBounds: new Rect(
                railOrigin.X,
                railOrigin.Y,
                _persistentVerticalScrollRail.ActualWidth,
                _persistentVerticalScrollRail.ActualHeight),
            ThumbBounds: new Rect(
                thumbOrigin.X,
                thumbOrigin.Y,
                _persistentVerticalScrollThumb.ActualWidth,
                _persistentVerticalScrollThumb.ActualHeight),
            RailColor: DescribePixelEvidenceBrush(
                _persistentVerticalScrollRail.Background),
            ThumbColor: DescribePixelEvidenceBrush(
                _persistentVerticalScrollThumb.Background),
            ThumbOpacity: _persistentVerticalScrollThumb.Opacity);
    }

    private static string DescribePixelEvidenceBrush(Brush? brush) =>
        brush is SolidColorBrush solid
            ? solid.Color.ToString()
            : brush?.GetType().Name ?? string.Empty;

    internal sealed record PersistentScrollbarPixelEvidence(
        bool IsVisible,
        string ActualTheme,
        Rect RailBounds,
        Rect ThumbBounds,
        string RailColor,
        string ThumbColor,
        double ThumbOpacity)
    {
        internal static PersistentScrollbarPixelEvidence Empty { get; } = new(
            false,
            string.Empty,
            Rect.Empty,
            Rect.Empty,
            string.Empty,
            string.Empty,
            0);
    }
}
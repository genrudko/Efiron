using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView
{
    private const double PersistentScrollBarWidth = 18;
    private const double PersistentScrollThumbWidth = 10;
    private const double PersistentScrollThumbMinimumHeight = 36;

    private Grid? _persistentVerticalScrollRail;
    private Border? _persistentVerticalScrollThumb;
    private bool _persistentVerticalScrollHooked;
    private bool _persistentVerticalScrollDragging;
    private bool _persistentVerticalScrollPointerOver;
    private uint _persistentVerticalScrollPointerId;
    private double _persistentVerticalScrollDragOffset;

    internal PersistentVerticalScrollBarEvidence
        GetPersistentVerticalScrollBarEvidence()
    {
        EnsurePersistentVerticalScrollBar();
        UpdatePersistentVerticalScrollBar();
        return new PersistentVerticalScrollBarEvidence(
            _persistentVerticalScrollRail?.Visibility == Visibility.Visible,
            _persistentVerticalScrollRail?.ActualWidth ?? 0,
            _persistentVerticalScrollRail?.ActualHeight ?? 0,
            _persistentVerticalScrollThumb?.ActualWidth ?? 0,
            _persistentVerticalScrollThumb?.ActualHeight ?? 0,
            _persistentVerticalScrollThumb?.Margin.Top ?? 0,
            EpgVerticalScrollBar.Minimum,
            EpgVerticalScrollBar.Maximum,
            EpgVerticalScrollBar.Value,
            EpgVerticalScrollBar.ViewportSize,
            _persistentVerticalScrollDragging);
    }

    private void EnsurePersistentVerticalScrollBar()
    {
        if (_persistentVerticalScrollHooked)
        {
            UpdatePersistentVerticalScrollBar();
            return;
        }

        _persistentVerticalScrollHooked = true;
        _persistentVerticalScrollThumb = new Border
        {
            Width = PersistentScrollThumbWidth,
            MinHeight = PersistentScrollThumbMinimumHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(PersistentScrollThumbWidth / 2),
            Background = ResolveBrush("EfironTextTertiaryBrush"),
        };
        _persistentVerticalScrollThumb.PointerPressed +=
            PersistentVerticalScrollThumb_PointerPressed;
        _persistentVerticalScrollThumb.PointerMoved +=
            PersistentVerticalScrollThumb_PointerMoved;
        _persistentVerticalScrollThumb.PointerReleased +=
            PersistentVerticalScrollThumb_PointerReleased;
        _persistentVerticalScrollThumb.PointerCanceled +=
            PersistentVerticalScrollThumb_PointerCanceled;
        _persistentVerticalScrollThumb.PointerCaptureLost +=
            PersistentVerticalScrollThumb_PointerCaptureLost;
        _persistentVerticalScrollThumb.PointerEntered +=
            PersistentVerticalScrollThumb_PointerEntered;
        _persistentVerticalScrollThumb.PointerExited +=
            PersistentVerticalScrollThumb_PointerExited;

        _persistentVerticalScrollRail = new Grid
        {
            Width = PersistentScrollBarWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
            Background = ResolveBrush("EfironSurfaceRaisedBrush"),
        };
        _persistentVerticalScrollRail.Children.Add(
            _persistentVerticalScrollThumb);
        _persistentVerticalScrollRail.PointerPressed +=
            PersistentVerticalScrollRail_PointerPressed;
        _persistentVerticalScrollRail.PointerWheelChanged +=
            PersistentVerticalScrollRail_PointerWheelChanged;
        _persistentVerticalScrollRail.SizeChanged +=
            PersistentVerticalScrollRail_SizeChanged;

        Grid.SetRow(_persistentVerticalScrollRail, 1);
        Grid.SetColumn(_persistentVerticalScrollRail, 2);
        Canvas.SetZIndex(_persistentVerticalScrollRail, 50);
        EpgSurfaceGrid.Children.Add(_persistentVerticalScrollRail);

        EpgVerticalScrollBar.ValueChanged +=
            PersistentVerticalScrollRange_ValueChanged;
        EpgRowsViewport.SizeChanged +=
            PersistentVerticalScrollViewport_SizeChanged;
        ProgrammeRoot.ActualThemeChanged +=
            PersistentVerticalScrollTheme_ActualThemeChanged;

        UpdatePersistentVerticalScrollBar();
    }

    private void UpdatePersistentVerticalScrollBar()
    {
        if (_persistentVerticalScrollRail is null ||
            _persistentVerticalScrollThumb is null)
        {
            return;
        }

        var minimum = EpgVerticalScrollBar.Minimum;
        var maximum = Math.Max(minimum, EpgVerticalScrollBar.Maximum);
        var range = maximum - minimum;
        var canScroll = range > 0.5 &&
            EpgVerticalScrollBar.ViewportSize > 0 &&
            EpgRowsViewport.ActualHeight > 0;

        EpgVerticalScrollBar.Opacity = 0;
        EpgVerticalScrollBar.IsHitTestVisible = false;
        EpgVerticalScrollBar.IsTabStop = false;

        _persistentVerticalScrollRail.Visibility = canScroll
            ? Visibility.Visible
            : Visibility.Collapsed;
        _persistentVerticalScrollRail.IsHitTestVisible = canScroll;
        if (!canScroll)
        {
            return;
        }

        _persistentVerticalScrollRail.Background =
            ResolveBrush("EfironSurfaceRaisedBrush");
        var active = _persistentVerticalScrollDragging ||
            _persistentVerticalScrollPointerOver;
        _persistentVerticalScrollThumb.Background = ResolveBrush(
            active ? "EfironAccentBrush" : "EfironTextTertiaryBrush");
        _persistentVerticalScrollThumb.Opacity =
            _persistentVerticalScrollDragging
                ? 1
                : _persistentVerticalScrollPointerOver
                    ? 0.92
                    : 0.72;

        var trackHeight = Math.Max(
            _persistentVerticalScrollRail.ActualHeight,
            EpgRowsViewport.ActualHeight);
        if (trackHeight <= 0)
        {
            return;
        }

        var viewport = Math.Max(1, EpgVerticalScrollBar.ViewportSize);
        var content = Math.Max(viewport, viewport + range);
        var minimumThumb = Math.Min(
            PersistentScrollThumbMinimumHeight,
            trackHeight);
        var thumbHeight = Math.Clamp(
            trackHeight * viewport / content,
            minimumThumb,
            trackHeight);
        var travel = Math.Max(0, trackHeight - thumbHeight);
        var normalized = range <= 0
            ? 0
            : Math.Clamp(
                (EpgVerticalScrollBar.Value - minimum) / range,
                0,
                1);
        var thumbTop = travel * normalized;

        if (double.IsNaN(_persistentVerticalScrollThumb.Height) ||
            Math.Abs(_persistentVerticalScrollThumb.Height - thumbHeight) > 0.1)
        {
            _persistentVerticalScrollThumb.Height = thumbHeight;
        }

        var currentMargin = _persistentVerticalScrollThumb.Margin;
        if (Math.Abs(currentMargin.Top - thumbTop) > 0.1)
        {
            _persistentVerticalScrollThumb.Margin = new Thickness(
                0,
                thumbTop,
                0,
                0);
        }
    }

    private void SetPersistentVerticalScrollFromPointer(double pointerY)
    {
        if (_persistentVerticalScrollRail is null ||
            _persistentVerticalScrollThumb is null)
        {
            return;
        }

        var range = EpgVerticalScrollBar.Maximum -
            EpgVerticalScrollBar.Minimum;
        var travel = Math.Max(
            0,
            _persistentVerticalScrollRail.ActualHeight -
            _persistentVerticalScrollThumb.ActualHeight);
        if (range <= 0 || travel <= 0)
        {
            SetVerticalOffset(EpgVerticalScrollBar.Minimum);
            return;
        }

        var thumbTop = Math.Clamp(
            pointerY - _persistentVerticalScrollDragOffset,
            0,
            travel);
        var requested = EpgVerticalScrollBar.Minimum +
            thumbTop / travel * range;
        SetVerticalOffset(requested);
        UpdatePersistentVerticalScrollBar();
    }

    private void PersistentVerticalScrollRail_PointerPressed(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (_persistentVerticalScrollRail is null ||
            _persistentVerticalScrollThumb is null ||
            _persistentVerticalScrollDragging)
        {
            return;
        }

        var point = e.GetCurrentPoint(_persistentVerticalScrollRail);
        _persistentVerticalScrollDragOffset =
            _persistentVerticalScrollThumb.ActualHeight / 2;
        SetPersistentVerticalScrollFromPointer(point.Position.Y);
        e.Handled = true;
    }

    private void PersistentVerticalScrollRail_PointerWheelChanged(
        object sender,
        PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(_persistentVerticalScrollRail)
            .Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        var sourceOffset = _smoothVerticalScrollActive
            ? _targetVerticalOffset
            : _verticalOffset;
        SetVerticalOffset(
            sourceOffset - Math.Sign(delta) * RowHeight * 3);
        e.Handled = true;
    }

    private void PersistentVerticalScrollThumb_PointerPressed(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (_persistentVerticalScrollRail is null ||
            _persistentVerticalScrollThumb is null)
        {
            return;
        }

        if (!_persistentVerticalScrollThumb.CapturePointer(e.Pointer))
        {
            return;
        }

        var point = e.GetCurrentPoint(_persistentVerticalScrollRail);
        _persistentVerticalScrollDragging = true;
        _persistentVerticalScrollPointerId = e.Pointer.PointerId;
        _persistentVerticalScrollDragOffset = Math.Clamp(
            point.Position.Y - _persistentVerticalScrollThumb.Margin.Top,
            0,
            _persistentVerticalScrollThumb.ActualHeight);
        UpdatePersistentVerticalScrollBar();
        e.Handled = true;
    }

    private void PersistentVerticalScrollThumb_PointerMoved(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (!_persistentVerticalScrollDragging ||
            _persistentVerticalScrollRail is null ||
            e.Pointer.PointerId != _persistentVerticalScrollPointerId)
        {
            return;
        }

        var point = e.GetCurrentPoint(_persistentVerticalScrollRail);
        SetPersistentVerticalScrollFromPointer(point.Position.Y);
        e.Handled = true;
    }

    private void PersistentVerticalScrollThumb_PointerReleased(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId != _persistentVerticalScrollPointerId)
        {
            return;
        }

        EndPersistentVerticalScrollDrag();
        e.Handled = true;
    }

    private void PersistentVerticalScrollThumb_PointerCanceled(
        object sender,
        PointerRoutedEventArgs e) =>
        EndPersistentVerticalScrollDrag();

    private void PersistentVerticalScrollThumb_PointerCaptureLost(
        object sender,
        PointerRoutedEventArgs e)
    {
        _persistentVerticalScrollDragging = false;
        _persistentVerticalScrollPointerId = 0;
        UpdatePersistentVerticalScrollBar();
    }

    private void PersistentVerticalScrollThumb_PointerEntered(
        object sender,
        PointerRoutedEventArgs e)
    {
        _persistentVerticalScrollPointerOver = true;
        UpdatePersistentVerticalScrollBar();
    }

    private void PersistentVerticalScrollThumb_PointerExited(
        object sender,
        PointerRoutedEventArgs e)
    {
        _persistentVerticalScrollPointerOver = false;
        UpdatePersistentVerticalScrollBar();
    }

    private void EndPersistentVerticalScrollDrag()
    {
        _persistentVerticalScrollThumb?.ReleasePointerCaptures();
        _persistentVerticalScrollDragging = false;
        _persistentVerticalScrollPointerId = 0;
        UpdatePersistentVerticalScrollBar();
    }

    private void PersistentVerticalScrollRange_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        UpdatePersistentVerticalScrollBar();

    private void PersistentVerticalScrollViewport_SizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        UpdatePersistentVerticalScrollBar();

    private void PersistentVerticalScrollRail_SizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        UpdatePersistentVerticalScrollBar();

    private void PersistentVerticalScrollTheme_ActualThemeChanged(
        FrameworkElement sender,
        object args) =>
        UpdatePersistentVerticalScrollBar();

    internal sealed record PersistentVerticalScrollBarEvidence(
        bool IsVisible,
        double RailWidth,
        double RailHeight,
        double ThumbWidth,
        double ThumbHeight,
        double ThumbTop,
        double Minimum,
        double Maximum,
        double Value,
        double ViewportSize,
        bool IsDragging);
}

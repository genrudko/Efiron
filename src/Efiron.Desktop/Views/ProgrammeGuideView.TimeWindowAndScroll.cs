using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView
{
    private const double DefaultTimeWindowHours = 6;

    private double _timeWindowHours = DefaultTimeWindowHours;
    private bool _timeWindowInitialized;
    private double _targetVerticalOffset;
    private bool _smoothVerticalScrollActive;
    private int _smoothRenderedFirstIndex = -1;

    private void ProgrammeRoot_TimeWindowSizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        ProgrammeRoot_SizeChanged(sender, e);
        ApplyTimeWindowScale(preserveCenter: _timeWindowInitialized);
    }

    private void ApplyTimeWindowScale(bool preserveCenter)
    {
        var viewport = TimelineViewportWidth;
        if (viewport <= 0)
        {
            return;
        }

        var oldPixelsPerMinute = Math.Max(0.01, _pixelsPerMinute);
        var centerMinute = (_horizontalOffset + viewport / 2) / oldPixelsPerMinute;

        _pixelsPerMinute = viewport / (_timeWindowHours * 60);
        TimelineZoomText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:0} ч",
            _timeWindowHours);
        TimelineZoomSlider.Visibility = Visibility.Collapsed;
        UpdateTimeWindowButtons();
        UpdateScrollRanges();

        var requestedOffset = preserveCenter
            ? centerMinute * _pixelsPerMinute - viewport / 2
            : 0;
        _timeWindowInitialized = true;
        SetHorizontalOffset(requestedOffset);
        UpdateTimeNavigationButtons();
    }

    private void TimeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string value } ||
            !double.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var hours))
        {
            return;
        }

        _timeWindowHours = Math.Clamp(hours, 3, 24);
        ApplyTimeWindowScale(preserveCenter: true);
    }

    private void PreviousTimeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        SetHorizontalOffset(_horizontalOffset - TimelineViewportWidth);
        UpdateTimeNavigationButtons();
    }

    private void NextTimeWindowButton_Click(object sender, RoutedEventArgs e)
    {
        SetHorizontalOffset(_horizontalOffset + TimelineViewportWidth);
        UpdateTimeNavigationButtons();
    }

    private void EpgHorizontalScrollBar_TimeWindowValueChanged(
        object sender,
        RangeBaseValueChangedEventArgs e)
    {
        EpgHorizontalScrollBar_ValueChanged(sender, e);
        UpdateTimeNavigationButtons();
    }

    private void UpdateTimeWindowButtons()
    {
        TimeWindow3HoursButton.IsChecked = Math.Abs(_timeWindowHours - 3) < 0.01;
        TimeWindow6HoursButton.IsChecked = Math.Abs(_timeWindowHours - 6) < 0.01;
        TimeWindow12HoursButton.IsChecked = Math.Abs(_timeWindowHours - 12) < 0.01;
        TimeWindow24HoursButton.IsChecked = Math.Abs(_timeWindowHours - 24) < 0.01;
    }

    private void UpdateTimeNavigationButtons()
    {
        var maximum = Math.Max(0, TimelineWidth - TimelineViewportWidth);
        PreviousTimeWindowButton.IsEnabled = _horizontalOffset > 0.5;
        NextTimeWindowButton.IsEnabled = _horizontalOffset < maximum - 0.5;
    }

    private void EpgRowsViewport_SmoothPointerWheelChanged(
        object sender,
        PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(EpgRowsViewport).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        var maximum = Math.Max(
            0,
            _visibleRows.Count * RowHeight - EpgRowsViewport.ActualHeight);
        var notches = delta / 120.0;
        var sourceOffset = _smoothVerticalScrollActive
            ? _targetVerticalOffset
            : _verticalOffset;
        _targetVerticalOffset = Math.Clamp(
            sourceOffset - notches * RowHeight * 0.9,
            0,
            maximum);

        if (!_smoothVerticalScrollActive)
        {
            _smoothVerticalScrollActive = true;
            _smoothRenderedFirstIndex = Math.Max(
                0,
                (int)Math.Floor(_verticalOffset / RowHeight));
            CompositionTarget.Rendering += SmoothVerticalScroll_Rendering;
        }

        e.Handled = true;
    }

    private void SmoothVerticalScroll_Rendering(object? sender, object e)
    {
        var remaining = _targetVerticalOffset - _verticalOffset;
        if (Math.Abs(remaining) < 0.35)
        {
            ApplySmoothVerticalOffset(_targetVerticalOffset, forceRender: true);
            StopSmoothVerticalScroll();
            return;
        }

        ApplySmoothVerticalOffset(
            _verticalOffset + remaining * 0.32,
            forceRender: false);
    }

    private void ApplySmoothVerticalOffset(double value, bool forceRender)
    {
        var maximum = Math.Max(
            0,
            _visibleRows.Count * RowHeight - EpgRowsViewport.ActualHeight);
        _verticalOffset = Math.Clamp(value, 0, maximum);

        _updatingScrollBars = true;
        EpgVerticalScrollBar.Value = _verticalOffset;
        _updatingScrollBars = false;

        var firstIndex = Math.Max(
            0,
            (int)Math.Floor(_verticalOffset / RowHeight));
        if (forceRender ||
            firstIndex != _smoothRenderedFirstIndex ||
            _rowVisualPool.Count == 0)
        {
            RenderViewport();
            _smoothRenderedFirstIndex = firstIndex;
            return;
        }

        var fractionalOffset = _verticalOffset - firstIndex * RowHeight;
        for (var poolIndex = 0; poolIndex < _rowVisualPool.Count; poolIndex++)
        {
            Canvas.SetTop(
                _rowVisualPool[poolIndex].Root,
                poolIndex * RowHeight - fractionalOffset);
        }

        UpdateCurrentTimeMarker();
    }

    private void EpgVerticalScrollBar_SmoothValueChanged(
        object sender,
        RangeBaseValueChangedEventArgs e)
    {
        if (_updatingScrollBars)
        {
            return;
        }

        StopSmoothVerticalScroll();
        _verticalOffset = e.NewValue;
        _targetVerticalOffset = e.NewValue;
        _smoothRenderedFirstIndex = Math.Max(
            0,
            (int)Math.Floor(_verticalOffset / RowHeight));
        RenderViewport();
    }

    private void ProgrammeRoot_SmoothUnloaded(object sender, RoutedEventArgs e) =>
        StopSmoothVerticalScroll();

    private void StopSmoothVerticalScroll()
    {
        if (!_smoothVerticalScrollActive)
        {
            return;
        }

        CompositionTarget.Rendering -= SmoothVerticalScroll_Rendering;
        _smoothVerticalScrollActive = false;
    }
}

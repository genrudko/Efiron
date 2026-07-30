using System.Diagnostics;
using System.Globalization;
using Efiron.Desktop.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView
{
    private const double DefaultTimeWindowHours = 6;
    private const int SmoothVerticalOverscanRows = 4;
    private const double SmoothVerticalResponse = 20;

    private double _timeWindowHours = DefaultTimeWindowHours;
    private bool _timeWindowInitialized;
    private double _targetVerticalOffset;
    private bool _smoothVerticalScrollActive;
    private long _smoothVerticalLastTimestamp;

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
            sourceOffset - notches * RowHeight * 1.35,
            0,
            maximum);

        if (!_smoothVerticalScrollActive)
        {
            _smoothVerticalScrollActive = true;
            _smoothVerticalLastTimestamp = Stopwatch.GetTimestamp();
            RenderSmoothVerticalViewport(forceRebind: false);
            CompositionTarget.Rendering += SmoothVerticalScroll_Rendering;
        }

        e.Handled = true;
    }

    private void SmoothVerticalScroll_Rendering(object? sender, object e)
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _smoothVerticalLastTimestamp) /
            (double)Stopwatch.Frequency;
        _smoothVerticalLastTimestamp = now;
        var frameSeconds = Math.Clamp(elapsed, 1.0 / 240.0, 1.0 / 20.0);

        var remaining = _targetVerticalOffset - _verticalOffset;
        if (Math.Abs(remaining) < 0.25)
        {
            ApplySmoothVerticalOffset(_targetVerticalOffset);
            StopSmoothVerticalScroll();
            return;
        }

        var interpolation = 1 - Math.Exp(-SmoothVerticalResponse * frameSeconds);
        ApplySmoothVerticalOffset(
            _verticalOffset + remaining * interpolation);
    }

    private void ApplySmoothVerticalOffset(double value)
    {
        var maximum = Math.Max(
            0,
            _visibleRows.Count * RowHeight - EpgRowsViewport.ActualHeight);
        _verticalOffset = Math.Clamp(value, 0, maximum);

        _updatingScrollBars = true;
        EpgVerticalScrollBar.Value = _verticalOffset;
        _updatingScrollBars = false;

        RenderSmoothVerticalViewport(forceRebind: false);
    }

    private void RenderSmoothVerticalViewport(bool forceRebind)
    {
        var width = EpgRowsViewport.ActualWidth;
        var height = EpgRowsViewport.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var firstVisibleIndex = Math.Max(
            0,
            (int)Math.Floor(_verticalOffset / RowHeight));
        var visibleCapacity = Math.Max(
            1,
            (int)Math.Ceiling(height / RowHeight) + 1);
        var desiredStart = Math.Max(
            0,
            firstVisibleIndex - SmoothVerticalOverscanRows);
        var desiredEnd = Math.Min(
            _visibleRows.Count,
            firstVisibleIndex + visibleCapacity + SmoothVerticalOverscanRows);
        var required = Math.Max(0, desiredEnd - desiredStart);
        EnsureRowVisualPool(required);

        var retained = new Dictionary<int, EpgRowVisual>();
        var available = new Queue<EpgRowVisual>();
        foreach (var visual in _rowVisualPool)
        {
            var canRetain = !forceRebind &&
                visual.BoundRowIndex >= desiredStart &&
                visual.BoundRowIndex < desiredEnd &&
                !retained.ContainsKey(visual.BoundRowIndex);
            if (canRetain)
            {
                retained.Add(visual.BoundRowIndex, visual);
            }
            else
            {
                visual.BoundRowIndex = -1;
                visual.Root.Visibility = Visibility.Collapsed;
                available.Enqueue(visual);
            }
        }

        for (var rowIndex = desiredStart; rowIndex < desiredEnd; rowIndex++)
        {
            if (!retained.TryGetValue(rowIndex, out var visual))
            {
                if (available.Count == 0)
                {
                    break;
                }

                visual = available.Dequeue();
                UpdateRowVisual(
                    visual,
                    _visibleRows[rowIndex],
                    rowIndex,
                    width);
            }

            visual.Root.Visibility = Visibility.Visible;
            visual.Root.Width = width;
            visual.Root.Height = RowHeight;
            Canvas.SetTop(
                visual.Root,
                rowIndex * RowHeight - _verticalOffset);
        }

        while (available.Count > 0)
        {
            var visual = available.Dequeue();
            visual.Root.Visibility = Visibility.Collapsed;
        }

        EpgRowsCanvas.Width = width;
        EpgRowsCanvas.Height = height;
        EpgRowsCanvas.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, width, height),
        };
        RebuildRealizedProgrammeButtonIndex();
        UpdateCurrentTimeMarker();
    }

    private void RebuildRealizedProgrammeButtonIndex()
    {
        _realizedProgrammeButtons.Clear();
        foreach (var visual in _rowVisualPool)
        {
            if (visual.Root.Visibility != Visibility.Visible)
            {
                continue;
            }

            foreach (var child in visual.ProgrammeCanvas.Children)
            {
                if (child is Button { Tag: EpgProgrammeBlockItem block } button)
                {
                    _realizedProgrammeButtons[ProgrammeVisualKey.From(block)] = button;
                }
            }
        }
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
        RenderSmoothVerticalViewport(forceRebind: false);
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
        _smoothVerticalLastTimestamp = 0;
    }
}

using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView
{
    private double TimelineWidth => MinutesPerDay * _pixelsPerMinute;

    private double TimelineViewportWidth =>
        Math.Max(0, EpgRowsViewport.ActualWidth - _channelColumnWidth);

    private void ProgrammeGuideView_Loaded(object sender, RoutedEventArgs e)
    {
        _clockTimer ??= DispatcherQueue.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(30);
        _clockTimer.IsRepeating = true;
        _clockTimer.Tick -= ClockTimer_Tick;
        _clockTimer.Tick += ClockTimer_Tick;
        _clockTimer.Start();
        UpdateScrollRanges();
        QueueViewportRender();
    }

    private void ProgrammeGuideView_Unloaded(object sender, RoutedEventArgs e)
    {
        _clockTimer?.Stop();
        _filterDebounceCancellation?.Cancel();
        _programmeProjectionCancellation?.Cancel();
    }

    private void ProgrammeRoot_ActualThemeChanged(
        FrameworkElement sender,
        object args)
    {
        EpgRowsCanvas.Children.Clear();
        _rowVisualPool.Clear();
        _realizedBandStart = -1;
        _realizedBandEnd = -1;
        BuildTimelineHeader();
        QueueViewportRender();
    }

    private void ClockTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        BuildTimelineHeader();
        UpdateCurrentTimeMarker();
    }

    private void BuildTimelineHeader()
    {
        TimelineHeaderCanvas.Children.Clear();
        var viewportWidth = TimelineViewportWidth;
        if (viewportWidth <= 0)
        {
            return;
        }

        TimelineHeaderCanvas.Width = viewportWidth;
        var visibleStartMinute = _horizontalOffset / _pixelsPerMinute;
        var visibleEndMinute =
            (_horizontalOffset + viewportWidth) / _pixelsPerMinute;
        var step = _pixelsPerMinute switch
        {
            < 1.0 => 120,
            < 2.0 => 60,
            _ => 30,
        };
        var first = Math.Max(0, (int)Math.Floor(visibleStartMinute / step) * step);
        var stroke = ResolveBrush("EfironStrokeSubtleBrush");
        var textBrush = ResolveBrush("EfironTextSecondaryBrush");

        for (var minute = first; minute <= visibleEndMinute + step; minute += step)
        {
            if (minute < 0 || minute > MinutesPerDay)
            {
                continue;
            }

            var x = minute * _pixelsPerMinute - _horizontalOffset;
            var line = new Border
            {
                Width = 1,
                Height = 14,
                Background = stroke,
                Opacity = minute % 60 == 0 ? 0.72 : 0.28,
            };
            Canvas.SetLeft(line, x);
            Canvas.SetTop(line, minute % 60 == 0 ? 31 : 38);
            TimelineHeaderCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = TimeOnly.MinValue.AddMinutes(minute % (24 * 60)).ToString(
                    "HH:mm",
                    CultureInfo.CurrentCulture),
                Foreground = textBrush,
                FontSize = 11,
                FontWeight = minute % 60 == 0
                    ? FontWeights.SemiBold
                    : FontWeights.Normal,
            };
            Canvas.SetLeft(label, x + 7);
            Canvas.SetTop(label, 16);
            TimelineHeaderCanvas.Children.Add(label);
        }
    }

    private void UpdateCurrentTimeMarker()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var viewportWidth = TimelineViewportWidth;
        if (_selectedDate != today || viewportWidth <= 0)
        {
            CurrentTimeLine.Visibility = Visibility.Collapsed;
            return;
        }

        var nowX = (DateTimeOffset.Now - GetSelectedDayStart()).TotalMinutes *
            _pixelsPerMinute;
        var viewportX = _channelColumnWidth + nowX - _horizontalOffset;
        var visible = viewportX >= _channelColumnWidth &&
            viewportX <= _channelColumnWidth + viewportWidth;
        CurrentTimeLine.Visibility = visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!visible)
        {
            return;
        }

        CurrentTimeLine.Height = EpgRowsViewport.ActualHeight;
        CurrentTimeLine.Margin = new Thickness(viewportX, 0, 0, 0);
    }

    private void UpdateScrollRanges()
    {
        _updatingScrollBars = true;
        try
        {
            var viewportHeight = Math.Max(0, EpgRowsViewport.ActualHeight);
            var viewportWidth = TimelineViewportWidth;
            EpgVerticalScrollBar.Minimum = 0;
            EpgVerticalScrollBar.Maximum = Math.Max(
                0,
                _visibleRows.Count * RowHeight - viewportHeight);
            EpgVerticalScrollBar.ViewportSize = viewportHeight;
            EpgVerticalScrollBar.SmallChange = RowHeight;
            EpgVerticalScrollBar.LargeChange = Math.Max(RowHeight, viewportHeight * 0.85);
            _verticalOffset = Math.Clamp(
                _verticalOffset,
                0,
                EpgVerticalScrollBar.Maximum);
            _targetVerticalOffset = _verticalOffset;
            EpgVerticalScrollBar.Value = _verticalOffset;

            EpgHorizontalScrollBar.Minimum = 0;
            EpgHorizontalScrollBar.Maximum = Math.Max(
                0,
                TimelineWidth - viewportWidth);
            EpgHorizontalScrollBar.ViewportSize = viewportWidth;
            EpgHorizontalScrollBar.SmallChange = Math.Max(30, 15 * _pixelsPerMinute);
            EpgHorizontalScrollBar.LargeChange = Math.Max(60, viewportWidth * 0.8);
            _horizontalOffset = Math.Clamp(
                _horizontalOffset,
                0,
                EpgHorizontalScrollBar.Maximum);
            EpgHorizontalScrollBar.Value = _horizontalOffset;
        }
        finally
        {
            _updatingScrollBars = false;
        }

        BuildTimelineHeader();
        UpdateCurrentTimeMarker();
    }

    private void SetHorizontalOffset(double value)
    {
        _horizontalOffset = Math.Clamp(
            value,
            0,
            Math.Max(0, TimelineWidth - TimelineViewportWidth));
        _updatingScrollBars = true;
        EpgHorizontalScrollBar.Value = _horizontalOffset;
        _updatingScrollBars = false;
        BuildTimelineHeader();
        UpdateCurrentTimeMarker();
        QueueViewportRender();
    }

    private void SetVerticalOffset(double value)
    {
        StopSmoothVerticalScroll();
        _verticalOffset = Math.Clamp(
            value,
            0,
            Math.Max(0, _visibleRows.Count * RowHeight - EpgRowsViewport.ActualHeight));
        _targetVerticalOffset = _verticalOffset;
        _updatingScrollBars = true;
        EpgVerticalScrollBar.Value = _verticalOffset;
        _updatingScrollBars = false;
        RenderSmoothVerticalViewport(forceRebind: false);
    }

    private void QueueViewportRender()
    {
        if (_renderQueued || !IsLoaded)
        {
            return;
        }

        _renderQueued = true;
        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                _renderQueued = false;
                RenderViewport();
            });
    }

    private void RenderViewport() =>
        RenderSmoothVerticalViewport(forceRebind: true);

    private void EnsureRowVisualPool(int required)
    {
        while (_rowVisualPool.Count < required)
        {
            var visual = CreateRowVisual();
            _rowVisualPool.Add(visual);
            EpgRowsCanvas.Children.Add(visual.Root);
        }
    }

    private void TimelineZoomSlider_ValueChanged(
        object sender,
        RangeBaseValueChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            _pixelsPerMinute = e.NewValue;
            return;
        }

        var viewport = TimelineViewportWidth;
        var centerMinute = viewport > 0
            ? (_horizontalOffset + viewport / 2) / Math.Max(0.01, _pixelsPerMinute)
            : 0;
        _pixelsPerMinute = e.NewValue;
        TimelineZoomText.Text = FormatZoom(_pixelsPerMinute);
        UpdateScrollRanges();
        SetHorizontalOffset(centerMinute * _pixelsPerMinute - viewport / 2);
    }

    private void EpgVerticalScrollBar_ValueChanged(
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

    private void EpgHorizontalScrollBar_ValueChanged(
        object sender,
        RangeBaseValueChangedEventArgs e)
    {
        if (!_updatingScrollBars)
        {
            _horizontalOffset = e.NewValue;
            BuildTimelineHeader();
            UpdateCurrentTimeMarker();
            QueueViewportRender();
        }
    }

    private void EpgRowsViewport_PointerWheelChanged(
        object sender,
        PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(EpgRowsViewport).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        SetVerticalOffset(_verticalOffset - Math.Sign(delta) * RowHeight * 3);
        e.Handled = true;
    }

    private void EpgRowsViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateScrollRanges();
        QueueViewportRender();
    }

    private void ProgrammeRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _channelColumnWidth = e.NewSize.Width < 900 ? 208 : 252;
        EpgChannelColumn.Width = new GridLength(_channelColumnWidth);
        ProgrammeSummaryText.Visibility = e.NewSize.Width < 760
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateScrollRanges();
        QueueViewportRender();
    }
}

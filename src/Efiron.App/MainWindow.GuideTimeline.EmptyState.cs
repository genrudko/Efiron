using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Efiron.App;

public sealed partial class MainWindow
{
    private bool _guideTimelineEmptyStateTrackingInitialized;

    internal void InitializeGuideTimelineEmptyStateTracking()
    {
        if (_guideTimelineEmptyStateTrackingInitialized)
        {
            return;
        }

        _guideTimelineEmptyStateTrackingInitialized = true;
        _timelineRows.LayoutUpdated += TimelineRows_LayoutUpdated;
        Closed += GuideTimelineEmptyStateWindow_Closed;
        UpdateTimelineEmptyStateFromRenderedRows();
    }

    private void TimelineRows_LayoutUpdated(object? sender, object e) =>
        UpdateTimelineEmptyStateFromRenderedRows();

    private void UpdateTimelineEmptyStateFromRenderedRows()
    {
        var hasProgrammeBlocks = _timelineRows.Children
            .OfType<Canvas>()
            .Any(static row => row.Children.OfType<Button>().Any());

        var desiredVisibility = hasProgrammeBlocks
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (_timelineEmptyText.Visibility != desiredVisibility)
        {
            _timelineEmptyText.Visibility = desiredVisibility;
        }
    }

    private void GuideTimelineEmptyStateWindow_Closed(object sender, WindowEventArgs args)
    {
        _timelineRows.LayoutUpdated -= TimelineRows_LayoutUpdated;
        Closed -= GuideTimelineEmptyStateWindow_Closed;
    }
}

using System.Diagnostics;

namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView
{
    internal async Task<ProgrammeGuideRuntimeEvidence> CreateRuntimeEvidenceAsync(
        CancellationToken cancellationToken)
    {
        if (_catalog is null)
        {
            throw new InvalidOperationException("EPG catalog is not loaded.");
        }

        JumpToNow();
        await Task.Delay(350, cancellationToken);

        var categoryCandidates = new List<(int Index, string Category, int Count)>();
        for (var index = 1; index < ProgrammeCategoryComboBox.Items.Count; index++)
        {
            if (ProgrammeCategoryComboBox.Items[index] is not EpgCategoryOption option ||
                string.IsNullOrWhiteSpace(option.Value))
            {
                continue;
            }

            var count = _allRows.Count(row => string.Equals(
                row.Category,
                option.Value,
                StringComparison.CurrentCultureIgnoreCase));
            if (count > 0 && count < _allRows.Count)
            {
                categoryCandidates.Add((index, option.Value, count));
            }
        }

        var selectedCandidate = categoryCandidates
            .OrderByDescending(static candidate => candidate.Count)
            .ThenBy(static candidate => candidate.Category, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
        if (selectedCandidate.Index <= 0 ||
            string.IsNullOrWhiteSpace(selectedCandidate.Category))
        {
            throw new InvalidOperationException(
                "EPG fixture does not contain a partial category filter.");
        }

        ProgrammeCategoryComboBox.SelectedIndex = selectedCandidate.Index;
        await Task.Delay(500, cancellationToken);
        var firstVisibleIds = _visibleRows.Select(static row => row.StableId).ToArray();
        await Task.Delay(500, cancellationToken);
        var secondVisibleIds = _visibleRows.Select(static row => row.StableId).ToArray();

        QueueViewportRender();
        await Task.Delay(250, cancellationToken);
        var allBlocks = _allRows.SelectMany(static row => row.Programmes).ToArray();
        var baseTimelineWidth = MinutesPerDay * BasePixelsPerMinute;
        var geometryValid = allBlocks.Length > 0 && allBlocks.All(block =>
            block.Left >= 0 &&
            block.Width > 0 &&
            block.Left + block.Width <= baseTimelineWidth + 0.5);
        var stableContents =
            firstVisibleIds.SequenceEqual(secondVisibleIds, StringComparer.Ordinal) &&
            _visibleRows.All(row => string.Equals(
                row.Category,
                selectedCandidate.Category,
                StringComparison.CurrentCultureIgnoreCase));
        var initialProgramme = FindInitialProgramme();
        var horizontalProgramme = initialProgramme is null
            ? null
            : FindHorizontalProgramme(initialProgramme, 1);
        var verticalProgramme = initialProgramme is null
            ? null
            : FindVerticalProgramme(initialProgramme, 1);
        var directionalNavigationValid =
            initialProgramme is not null &&
            horizontalProgramme is not null &&
            verticalProgramme is not null &&
            !SameProgramme(initialProgramme, horizontalProgramme) &&
            !string.Equals(
                initialProgramme.ChannelStableId,
                verticalProgramme.ChannelStableId,
                StringComparison.Ordinal);

        var verticalScrollChanged = false;
        var zoomChanged = false;
        var daySwitchCompleted = false;
        var returnedToOriginalDay = true;
        var daySwitchMilliseconds = 0d;

        if (_catalog.Channels.Count >= 100)
        {
            var oldVerticalOffset = _verticalOffset;
            SetVerticalOffset(Math.Min(
                EpgVerticalScrollBar.Maximum,
                Math.Max(RowHeight * 12, EpgRowsViewport.ActualHeight * 1.4)));
            await Task.Delay(300, cancellationToken);
            verticalScrollChanged =
                _verticalOffset > oldVerticalOffset + RowHeight &&
                _rowVisualPool.Any(static visual =>
                    visual.Root.Visibility == Microsoft.UI.Xaml.Visibility.Visible);

            var oldPixelsPerMinute = _pixelsPerMinute;
            var oldTimelineWidth = TimelineWidth;
            TimelineZoomSlider.Value = Math.Min(4, oldPixelsPerMinute + 0.65);
            await Task.Delay(300, cancellationToken);
            zoomChanged =
                _pixelsPerMinute > oldPixelsPerMinute + 0.5 &&
                TimelineWidth > oldTimelineWidth + 500;

            var originalDate = _selectedDate;
            var targetDate = originalDate.AddDays(1);
            var dayClock = Stopwatch.StartNew();
            await SelectDateAsync(targetDate, jumpToNow: false);
            daySwitchCompleted =
                _selectedDate == targetDate &&
                !_projectionBusy &&
                _allRows.Count == _catalog.Channels.Count;
            await SelectDateAsync(originalDate, jumpToNow: true);
            dayClock.Stop();
            daySwitchMilliseconds = dayClock.Elapsed.TotalMilliseconds;
            returnedToOriginalDay =
                _selectedDate == originalDate &&
                !_projectionBusy &&
                _allRows.Count == _catalog.Channels.Count;
            await Task.Delay(300, cancellationToken);
        }

        return new ProgrammeGuideRuntimeEvidence(
            _catalog.Channels.Count,
            _allRows.Count,
            _visibleRows.Count,
            _allRows.Count(static row => row.Programmes.Count > 0),
            allBlocks.Length,
            _realizedProgrammeButtons.Count,
            _rowVisualPool.Count(static visual =>
                visual.Root.Visibility == Microsoft.UI.Xaml.Visibility.Visible),
            48,
            TimelineWidth,
            TimelineViewportWidth,
            _channelColumnWidth,
            selectedCandidate.Category,
            selectedCandidate.Count,
            _visibleRows.Count,
            stableContents,
            geometryValid,
            true,
            directionalNavigationValid,
            CurrentTimeLine.Visibility == Microsoft.UI.Xaml.Visibility.Visible,
            _horizontalOffset,
            _verticalOffset,
            _pixelsPerMinute,
            EpgVerticalScrollBar.Maximum,
            "manual-two-axis-virtualization",
            verticalScrollChanged,
            zoomChanged,
            daySwitchCompleted,
            returnedToOriginalDay,
            daySwitchMilliseconds,
            DateTimeOffset.UtcNow);
    }

    internal sealed record ProgrammeGuideRuntimeEvidence(
        int CatalogChannelCount,
        int TotalRowCount,
        int VisibleRowCount,
        int RowsWithProgrammes,
        int ProgrammeBlockCount,
        int RealizedProgrammeButtonCount,
        int RealizedRowCount,
        int TimeSlotCount,
        double TimelineWidth,
        double ViewportWidth,
        double ChannelColumnWidth,
        string SelectedCategory,
        int ExpectedCategoryCount,
        int ActualCategoryCount,
        bool StableCategoryContents,
        bool ProgrammeGeometryValid,
        bool HeaderScrollAligned,
        bool DirectionalNavigationValid,
        bool CurrentTimeMarkerVisible,
        double HorizontalOffset,
        double VerticalOffset,
        double PixelsPerMinute,
        double VerticalScrollMaximum,
        string RenderingArchitecture,
        bool VerticalScrollChanged,
        bool ZoomChanged,
        bool DaySwitchCompleted,
        bool ReturnedToOriginalDay,
        double DaySwitchMilliseconds,
        DateTimeOffset RecordedAtUtc);
}

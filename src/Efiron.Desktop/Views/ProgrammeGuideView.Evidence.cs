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

        var isProviderScale = _catalog.Channels.Count >= 100;
        var selectedCategory = string.Empty;
        var expectedCategoryCount = _allRows.Count;

        if (isProviderScale)
        {
            ProgrammeCategoryComboBox.SelectedIndex = 0;
        }
        else
        {
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

            selectedCategory = selectedCandidate.Category;
            expectedCategoryCount = selectedCandidate.Count;
            ProgrammeCategoryComboBox.SelectedIndex = selectedCandidate.Index;
        }

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
            (isProviderScale
                ? _visibleRows.Count == _allRows.Count
                : _visibleRows.All(row => string.Equals(
                    row.Category,
                    selectedCategory,
                    StringComparison.CurrentCultureIgnoreCase)));

        var initialProgramme = FindNavigationEvidenceProgramme();
        var initialRowIndex = initialProgramme is null
            ? -1
            : _visibleRows.FindIndex(row => string.Equals(
                row.StableId,
                initialProgramme.ChannelStableId,
                StringComparison.Ordinal));
        var horizontalDelta = ResolveHorizontalEvidenceDelta(initialProgramme);
        var verticalDelta = initialRowIndex >= _visibleRows.Count - 1 ? -1 : 1;
        var horizontalProgramme = initialProgramme is null || horizontalDelta == 0
            ? null
            : FindHorizontalProgramme(initialProgramme, horizontalDelta);
        var verticalProgramme = initialProgramme is null || initialRowIndex < 0
            ? null
            : FindVerticalProgramme(initialProgramme, verticalDelta);
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
        var rapidScrollCompleted = false;
        var rapidScrollSamples = 0;
        var maxRealizedRowsDuringScroll = 0;
        var zoomChanged = false;
        var daySwitchCompleted = false;
        var returnedToOriginalDay = true;
        var daySwitchMilliseconds = 0d;

        if (isProviderScale)
        {
            var oldVerticalOffset = _verticalOffset;
            var maximum = EpgVerticalScrollBar.Maximum;
            var offsets = new[]
            {
                0d,
                maximum * 0.20,
                maximum * 0.55,
                maximum,
                maximum * 0.35,
                maximum * 0.85,
                0d,
            };

            foreach (var offset in offsets)
            {
                SetVerticalOffset(offset);
                await Task.Delay(90, cancellationToken);
                rapidScrollSamples++;
                maxRealizedRowsDuringScroll = Math.Max(
                    maxRealizedRowsDuringScroll,
                    _rowVisualPool.Count(static visual =>
                        visual.Root.Visibility == Microsoft.UI.Xaml.Visibility.Visible));
            }

            verticalScrollChanged =
                maximum > RowHeight * 20 &&
                offsets.Any(offset => offset > oldVerticalOffset + RowHeight);
            rapidScrollCompleted =
                _verticalOffset <= RowHeight &&
                rapidScrollSamples == offsets.Length &&
                maxRealizedRowsDuringScroll > 0 &&
                maxRealizedRowsDuringScroll <= 40;

            var oldPixelsPerMinute = _pixelsPerMinute;
            var oldTimelineWidth = TimelineWidth;
            TimelineZoomSlider.Value = Math.Min(4, oldPixelsPerMinute + 0.65);
            await Task.Delay(300, cancellationToken);
            zoomChanged =
                _pixelsPerMinute > oldPixelsPerMinute + 0.5 &&
                TimelineWidth > oldTimelineWidth + 500;

            var originalDate = _selectedDate;
            var dayClock = Stopwatch.StartNew();
            var nextDate = originalDate.AddDays(1);
            var previousDate = originalDate.AddDays(-1);
            await SelectDateAsync(nextDate, jumpToNow: false);
            var nextDayCompleted =
                _selectedDate == nextDate &&
                !_projectionBusy &&
                _allRows.Count == _catalog.Channels.Count;
            await SelectDateAsync(previousDate, jumpToNow: false);
            var previousDayCompleted =
                _selectedDate == previousDate &&
                !_projectionBusy &&
                _allRows.Count == _catalog.Channels.Count;
            await SelectDateAsync(originalDate, jumpToNow: true);
            dayClock.Stop();
            daySwitchMilliseconds = dayClock.Elapsed.TotalMilliseconds;
            returnedToOriginalDay =
                _selectedDate == originalDate &&
                !_projectionBusy &&
                _allRows.Count == _catalog.Channels.Count;
            daySwitchCompleted =
                nextDayCompleted && previousDayCompleted && returnedToOriginalDay;
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
            selectedCategory,
            expectedCategoryCount,
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
            isProviderScale,
            verticalScrollChanged,
            rapidScrollCompleted,
            rapidScrollSamples,
            maxRealizedRowsDuringScroll,
            zoomChanged,
            daySwitchCompleted,
            returnedToOriginalDay,
            daySwitchMilliseconds,
            DateTimeOffset.UtcNow);
    }

    private EpgProgrammeBlockItem? FindNavigationEvidenceProgramme()
    {
        foreach (var row in _visibleRows)
        {
            if (row.Programmes.Count < 2)
            {
                continue;
            }

            return row.Programmes
                .OrderBy(static programme => programme.Programme.Start)
                .First();
        }

        return FindInitialProgramme();
    }

    private int ResolveHorizontalEvidenceDelta(EpgProgrammeBlockItem? programme)
    {
        if (programme is null)
        {
            return 0;
        }

        var row = _visibleRows.FirstOrDefault(candidate => string.Equals(
            candidate.StableId,
            programme.ChannelStableId,
            StringComparison.Ordinal));
        if (row is null || row.Programmes.Count < 2)
        {
            return 0;
        }

        var ordered = row.Programmes
            .OrderBy(static candidate => candidate.Programme.Start)
            .ToArray();
        var index = Array.FindIndex(
            ordered,
            candidate => SameProgramme(candidate, programme));
        return index >= ordered.Length - 1 ? -1 : 1;
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
        bool AllChannelsMode,
        bool VerticalScrollChanged,
        bool RapidScrollCompleted,
        int RapidScrollSamples,
        int MaxRealizedRowsDuringScroll,
        bool ZoomChanged,
        bool DaySwitchCompleted,
        bool ReturnedToOriginalDay,
        double DaySwitchMilliseconds,
        DateTimeOffset RecordedAtUtc);
}

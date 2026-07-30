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

        var categoryIndex = -1;
        string? category = null;
        var expectedCategoryCount = 0;
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
                categoryIndex = index;
                category = option.Value;
                expectedCategoryCount = count;
                break;
            }
        }

        if (categoryIndex < 0 || category is null)
        {
            throw new InvalidOperationException(
                "EPG fixture does not contain a partial category filter.");
        }

        ProgrammeCategoryComboBox.SelectedIndex = categoryIndex;
        await Task.Delay(850, cancellationToken);
        var firstVisibleIds = _visibleRows.Select(static row => row.StableId).ToArray();
        await Task.Delay(650, cancellationToken);
        var secondVisibleIds = _visibleRows.Select(static row => row.StableId).ToArray();

        var allBlocks = _allRows.SelectMany(static row => row.Programmes).ToArray();
        var geometryValid = allBlocks.Length > 0 && allBlocks.All(static block =>
            block.Left >= 0 &&
            block.Width > 0 &&
            block.Left + block.Width <= TimelineWidth + 0.5);
        var stableContents =
            firstVisibleIds.SequenceEqual(secondVisibleIds, StringComparer.Ordinal) &&
            _visibleRows.All(row => string.Equals(
                row.Category,
                category,
                StringComparison.CurrentCultureIgnoreCase));
        var headerAligned = Math.Abs(
            TimelineHeaderScrollViewer.HorizontalOffset -
            TimelineHorizontalScrollViewer.HorizontalOffset) < 1;

        return new ProgrammeGuideRuntimeEvidence(
            _catalog.Channels.Count,
            _allRows.Count,
            _visibleRows.Count,
            _allRows.Count(static row => row.Programmes.Count > 0),
            allBlocks.Length,
            TimelineHeaderGrid.Children.Count,
            TimelineHeaderGrid.Width,
            TimelineViewportGrid.ActualWidth,
            EpgChannelColumn.Width.Value,
            category,
            expectedCategoryCount,
            _visibleRows.Count,
            stableContents,
            geometryValid,
            headerAligned,
            CurrentTimeLine.Visibility == Microsoft.UI.Xaml.Visibility.Visible,
            TimelineHorizontalScrollViewer.HorizontalOffset,
            DateTimeOffset.UtcNow);
    }

    internal sealed record ProgrammeGuideRuntimeEvidence(
        int CatalogChannelCount,
        int TotalRowCount,
        int VisibleRowCount,
        int RowsWithProgrammes,
        int ProgrammeBlockCount,
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
        bool CurrentTimeMarkerVisible,
        double HorizontalOffset,
        DateTimeOffset RecordedAtUtc);
}

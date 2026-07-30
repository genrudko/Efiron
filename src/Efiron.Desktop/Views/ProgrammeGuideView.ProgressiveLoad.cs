using System.Diagnostics;
using System.Globalization;
using Efiron.Application.Live;
using Efiron.Desktop.Presentation;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView
{
    private const int ProgressiveRowBatchSize = 32;

    private CancellationTokenSource? _programmeProjectionCancellation;
    private LiveCatalogSnapshot? _projectedCatalog;
    private DateOnly? _projectedDate;
    private bool _progressiveFilterHandlersEnabled;
    private CancellationTokenSource? _filterDebounceCancellation;

    internal async Task SetCatalogProgressivelyAsync(
        LiveCatalogSnapshot catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        EnableProgressiveFilterHandlers();
        _programmeProjectionCancellation?.Cancel();
        _programmeProjectionCancellation?.Dispose();
        _programmeProjectionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _programmeProjectionCancellation.Token;

        _catalog = catalog;
        _selectedProgramme = null;
        ProgrammeDetailsCard.Visibility = Visibility.Collapsed;
        PopulateCategories();

        if (ReferenceEquals(_projectedCatalog, catalog) &&
            _projectedDate == _selectedDate &&
            _allRows.Count == catalog.Channels.Count)
        {
            await ApplyFiltersProgressivelyAsync(token);
            QueueJumpToNow();
            return;
        }

        SetProjectionBusy(true, catalog.Channels.Count);
        var dayStart = GetSelectedDayStart();
        var dayEnd = dayStart.AddDays(1);
        var now = DateTimeOffset.Now;
        var clock = Stopwatch.StartNew();

        try
        {
            var rows = await Task.Run(
                () => ProjectRows(catalog, dayStart, dayEnd, now, token),
                token);
            token.ThrowIfCancellationRequested();

            _allRows.Clear();
            _allRows.AddRange(rows);
            _projectedCatalog = catalog;
            _projectedDate = _selectedDate;

            await ApplyFiltersProgressivelyAsync(token);
            UpdateSelectedDateText();
            UpdateCurrentTimeMarker();
            QueueJumpToNow();
            await RecordProjectionDiagnosticsAsync(
                catalog,
                rows,
                clock.Elapsed,
                token);
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                SetProjectionBusy(false, catalog.Channels.Count);
            }
        }
    }

    private static EpgChannelRowItem[] ProjectRows(
        LiveCatalogSnapshot catalog,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = new EpgChannelRowItem[catalog.Channels.Count];
        for (var index = 0; index < catalog.Channels.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = catalog.Channels[index];
            var programmes = BuildProgrammeBlocks(
                channel,
                dayStart,
                dayEnd,
                now);
            rows[index] = new EpgChannelRowItem(
                index + 1,
                channel,
                programmes,
                TimelineWidth);
        }

        return rows;
    }

    private void EnableProgressiveFilterHandlers()
    {
        if (_progressiveFilterHandlersEnabled)
        {
            return;
        }

        _progressiveFilterHandlersEnabled = true;
        ProgrammeSearchTextBox.TextChanged -= ProgrammeSearchTextBox_TextChanged;
        ProgrammeCategoryComboBox.SelectionChanged -=
            ProgrammeCategoryComboBox_SelectionChanged;
        ProgrammeSearchTextBox.TextChanged +=
            ProgressiveProgrammeSearchTextBox_TextChanged;
        ProgrammeCategoryComboBox.SelectionChanged +=
            ProgressiveProgrammeCategoryComboBox_SelectionChanged;
    }

    private async void ProgressiveProgrammeSearchTextBox_TextChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
    {
        _filterDebounceCancellation?.Cancel();
        _filterDebounceCancellation?.Dispose();
        _filterDebounceCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                _programmeProjectionCancellation?.Token ?? CancellationToken.None);
        var token = _filterDebounceCancellation.Token;

        try
        {
            await Task.Delay(180, token);
            await ApplyFiltersProgressivelyAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async void ProgressiveProgrammeCategoryComboBox_SelectionChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (_updatingCategory)
        {
            return;
        }

        try
        {
            await ApplyFiltersProgressivelyAsync(
                _programmeProjectionCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ApplyFiltersProgressivelyAsync(CancellationToken cancellationToken)
    {
        var search = ProgrammeSearchTextBox.Text.Trim();
        var category =
            (ProgrammeCategoryComboBox.SelectedItem as EpgCategoryOption)?.Value;
        IEnumerable<EpgChannelRowItem> query = _allRows;

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(row => string.Equals(
                row.Category,
                category,
                StringComparison.CurrentCultureIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(row =>
                row.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                row.Programmes.Any(block => block.Title.Contains(
                    search,
                    StringComparison.CurrentCultureIgnoreCase)));
        }

        var filtered = query.ToArray();
        _visibleRows.Clear();

        for (var offset = 0; offset < filtered.Length; offset += ProgressiveRowBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(ProgressiveRowBatchSize, filtered.Length - offset);
            for (var index = 0; index < count; index++)
            {
                _visibleRows.Add(filtered[offset + index]);
            }

            if (offset + count < filtered.Length)
            {
                await Task.Delay(1, cancellationToken);
            }
        }

        var isEmpty = filtered.Length == 0;
        ProgrammeEmptyState.Visibility = isEmpty
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChannelRowsListView.Visibility = isEmpty
            ? Visibility.Collapsed
            : Visibility.Visible;
        TimelineViewportGrid.Visibility = isEmpty
            ? Visibility.Collapsed
            : Visibility.Visible;

        VisibleChannelCountText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0} каналов",
            filtered.Length);
        ProgrammeSummaryText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0} каналов · {1} с программой",
            filtered.Length,
            filtered.Count(static row => row.Programmes.Count > 0));
    }

    private void SetProjectionBusy(bool isBusy, int channelCount)
    {
        ProgrammeSearchTextBox.IsEnabled = !isBusy;
        ProgrammeCategoryComboBox.IsEnabled = !isBusy;
        JumpNowButton.IsEnabled = !isBusy;

        if (!isBusy)
        {
            return;
        }

        ProgrammeEmptyState.Visibility = Visibility.Collapsed;
        ChannelRowsListView.Visibility = Visibility.Collapsed;
        TimelineViewportGrid.Visibility = Visibility.Collapsed;
        VisibleChannelCountText.Text = "Загрузка…";
        ProgrammeSummaryText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "Подготовка программы: {0} каналов",
            channelCount);
    }

    private static async Task RecordProjectionDiagnosticsAsync(
        LiveCatalogSnapshot catalog,
        IReadOnlyList<EpgChannelRowItem> rows,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        var diagnosticsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Efiron",
            "diagnostics");
        Directory.CreateDirectory(diagnosticsDirectory);
        var evidence = new
        {
            CatalogChannelCount = catalog.Channels.Count,
            ProjectedRowCount = rows.Count,
            ProgrammeBlockCount = rows.Sum(static row => row.Programmes.Count),
            ProjectionMilliseconds = elapsed.TotalMilliseconds,
            RecordedAtUtc = DateTimeOffset.UtcNow,
        };
        await File.WriteAllTextAsync(
            Path.Combine(diagnosticsDirectory, "epg-projection-runtime.json"),
            System.Text.Json.JsonSerializer.Serialize(evidence),
            cancellationToken);
    }
}

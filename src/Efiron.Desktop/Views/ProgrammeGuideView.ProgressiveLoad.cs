using System.Diagnostics;
using Efiron.Application.Live;
using Efiron.Desktop.Presentation;
using Microsoft.UI.Xaml;

namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView
{
    private readonly Dictionary<DateOnly, EpgChannelRowItem[]> _projectionCache = [];
    private CancellationTokenSource? _programmeProjectionCancellation;
    private LiveCatalogSnapshot? _projectionCacheCatalog;
    private bool _projectionBusy;

    internal async Task SetCatalogProgressivelyAsync(
        LiveCatalogSnapshot catalog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        _programmeProjectionCancellation?.Cancel();
        _programmeProjectionCancellation?.Dispose();
        _programmeProjectionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _programmeProjectionCancellation.Token;

        if (!ReferenceEquals(_projectionCacheCatalog, catalog))
        {
            _projectionCache.Clear();
            _projectionCacheCatalog = catalog;
        }

        _catalog = catalog;
        _selectedProgramme = null;
        ProgrammeDetailsCard.Visibility = Visibility.Collapsed;
        PopulateCategories();
        UpdateSelectedDateText();

        if (_projectionCache.TryGetValue(_selectedDate, out var cachedRows))
        {
            _allRows.Clear();
            _allRows.AddRange(cachedRows);
            ApplyFilters();
            PositionAfterProjection();
            await RecordProjectionDiagnosticsAsync(
                catalog,
                cachedRows,
                TimeSpan.Zero,
                token);
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

            _projectionCache[_selectedDate] = rows;
            TrimProjectionCache();
            _allRows.Clear();
            _allRows.AddRange(rows);
            ApplyFilters();
            PositionAfterProjection();
            await Task.Yield();
            await RecordProjectionDiagnosticsAsync(
                catalog,
                rows,
                clock.Elapsed,
                token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                SetProjectionBusy(false, catalog.Channels.Count);
            }
        }
    }

    internal bool IsProjectionBusy => _projectionBusy;

    private void PositionAfterProjection()
    {
        if (_selectedDate == DateOnly.FromDateTime(DateTime.Today))
        {
            QueueJumpToNow();
        }
        else
        {
            SetHorizontalOffset(0);
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
            rows[index] = new EpgChannelRowItem(
                index + 1,
                channel,
                BuildProgrammeBlocks(channel, dayStart, dayEnd, now),
                MinutesPerDay * BasePixelsPerMinute);
        }

        return rows;
    }

    private void TrimProjectionCache()
    {
        if (_projectionCache.Count <= 5)
        {
            return;
        }

        var keysToRemove = _projectionCache.Keys
            .OrderByDescending(date => Math.Abs(date.DayNumber - _selectedDate.DayNumber))
            .Take(_projectionCache.Count - 5)
            .ToArray();
        foreach (var key in keysToRemove)
        {
            _projectionCache.Remove(key);
        }
    }

    private void SetProjectionBusy(bool isBusy, int channelCount)
    {
        _projectionBusy = isBusy;
        ProgrammeLoadingOverlay.Visibility = isBusy
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProgrammeLoadingText.Text = $"Подготовка программы: {channelCount} каналов";
        VisibleChannelCountText.Text = isBusy
            ? "Загрузка…"
            : $"{_visibleRows.Count} каналов";

        ProgrammeSearchTextBox.IsEnabled = !isBusy;
        ProgrammeCategoryComboBox.IsEnabled = !isBusy;
        JumpNowButton.IsEnabled = !isBusy;
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
            RenderingArchitecture = "manual-two-axis-virtualization",
            RecordedAtUtc = DateTimeOffset.UtcNow,
        };
        await File.WriteAllTextAsync(
            Path.Combine(diagnosticsDirectory, "epg-projection-runtime.json"),
            System.Text.Json.JsonSerializer.Serialize(evidence),
            cancellationToken);
    }
}

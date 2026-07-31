using System.Globalization;
using Efiron.Application.Live;
using Efiron.Desktop.Presentation;
using Efiron.Domain.ProgrammeGuide;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView : UserControl
{
    private const double BasePixelsPerMinute = 2;
    private const double MinutesPerDay = 24 * 60;
    private const double RowHeight = 86;
    private const double MinimumProgrammeWidth = 30;

    private readonly List<EpgChannelRowItem> _allRows = [];
    private readonly List<EpgChannelRowItem> _visibleRows = [];
    private readonly List<EpgRowVisual> _rowVisualPool = [];
    private readonly Dictionary<ProgrammeVisualKey, Button> _realizedProgrammeButtons = [];

    private LiveCatalogSnapshot? _catalog;
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private EpgProgrammeBlockItem? _selectedProgramme;
    private DispatcherQueueTimer? _clockTimer;
    private CancellationTokenSource? _filterDebounceCancellation;
    private bool _updatingCategory;
    private bool _updatingScrollBars;
    private bool _renderQueued;
    private double _pixelsPerMinute = 1.35;
    private double _horizontalOffset;
    private double _verticalOffset;
    private double _channelColumnWidth = 252;

    public ProgrammeGuideView()
    {
        InitializeComponent();
        UpdateSelectedDateText();
        TimelineZoomText.Text = FormatZoom(_pixelsPerMinute);
        Loaded += ProgrammeGuideView_Loaded;
        Unloaded += ProgrammeGuideView_Unloaded;
        ProgrammeRoot.ActualThemeChanged += ProgrammeRoot_ActualThemeChanged;
    }

    public event EventHandler<PlayChannelRequestedEventArgs>? PlayChannelRequested;

    public void FocusSearch() =>
        ProgrammeSearchTextBox.Focus(FocusState.Programmatic);

    private static IReadOnlyList<EpgProgrammeBlockItem> BuildProgrammeBlocks(
        LiveChannelSnapshot channel,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        DateTimeOffset now)
    {
        if (channel.Schedule.Count == 0)
        {
            return Array.Empty<EpgProgrammeBlockItem>();
        }

        var blocks = new List<EpgProgrammeBlockItem>();
        var baseTimelineWidth = MinutesPerDay * BasePixelsPerMinute;
        for (var index = 0; index < channel.Schedule.Count; index++)
        {
            var programme = channel.Schedule[index];
            var effectiveStop = programme.Stop ??
                (index + 1 < channel.Schedule.Count
                    ? channel.Schedule[index + 1].Start
                    : programme.Start.AddMinutes(30));
            if (programme.Start >= dayEnd || effectiveStop <= dayStart)
            {
                continue;
            }

            var clippedStart = programme.Start < dayStart ? dayStart : programme.Start;
            var clippedStop = effectiveStop > dayEnd ? dayEnd : effectiveStop;
            var left = Math.Clamp(
                (clippedStart - dayStart).TotalMinutes * BasePixelsPerMinute,
                0,
                baseTimelineWidth);
            var remainingWidth = Math.Max(0, baseTimelineWidth - left);
            var durationWidth = Math.Max(
                0,
                (clippedStop - clippedStart).TotalMinutes * BasePixelsPerMinute);
            var width = Math.Min(remainingWidth, Math.Max(4, durationWidth));
            if (width <= 0)
            {
                continue;
            }

            var localStart = programme.Start.ToLocalTime();
            var localStop = effectiveStop.ToLocalTime();
            blocks.Add(new EpgProgrammeBlockItem(
                channel.Channel.StableId,
                programme,
                left,
                width,
                $"{localStart:HH:mm}–{localStop:HH:mm}",
                programme.Start <= now && now < effectiveStop));
        }

        return blocks;
    }

    private void PopulateCategories()
    {
        if (_catalog is null)
        {
            return;
        }

        var previousValue =
            (ProgrammeCategoryComboBox.SelectedItem as EpgCategoryOption)?.Value;
        _updatingCategory = true;
        try
        {
            ProgrammeCategoryComboBox.Items.Clear();
            ProgrammeCategoryComboBox.Items.Add(
                new EpgCategoryOption("Все категории", null));
            foreach (var category in _catalog.Categories
                         .OrderBy(static value => value, StringComparer.CurrentCultureIgnoreCase))
            {
                ProgrammeCategoryComboBox.Items.Add(
                    new EpgCategoryOption(category, category));
            }

            ProgrammeCategoryComboBox.SelectedIndex = 0;
            if (!string.IsNullOrWhiteSpace(previousValue))
            {
                for (var index = 1; index < ProgrammeCategoryComboBox.Items.Count; index++)
                {
                    if (ProgrammeCategoryComboBox.Items[index] is EpgCategoryOption option &&
                        string.Equals(
                            option.Value,
                            previousValue,
                            StringComparison.CurrentCultureIgnoreCase))
                    {
                        ProgrammeCategoryComboBox.SelectedIndex = index;
                        break;
                    }
                }
            }
        }
        finally
        {
            _updatingCategory = false;
        }
    }

    private DateTimeOffset GetSelectedDayStart()
    {
        var localDateTime = DateTime.SpecifyKind(
            _selectedDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);
        return new DateTimeOffset(
            localDateTime,
            TimeZoneInfo.Local.GetUtcOffset(localDateTime));
    }

    private void UpdateSelectedDateText()
    {
        SelectedDateText.Text = _selectedDate
            .ToDateTime(TimeOnly.MinValue)
            .ToString("ddd, d MMMM", CultureInfo.CurrentCulture);
    }

    private async Task SelectDateAsync(DateOnly date, bool jumpToNow)
    {
        if (_catalog is null)
        {
            return;
        }

        _selectedDate = date;
        _selectedProgramme = null;
        _keyboardProgramme = null;
        ProgrammeDetailsCard.Visibility = Visibility.Collapsed;
        UpdateSelectedDateText();

        try
        {
            await SetCatalogProgressivelyAsync(_catalog, CancellationToken.None);
            if (jumpToNow)
            {
                QueueJumpToNow();
            }
            else
            {
                SetHorizontalOffset(0);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer date selection superseded this projection.
        }
    }

    private void QueueJumpToNow() =>
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, JumpToNow);

    private void JumpToNow()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (_selectedDate != today)
        {
            _ = SelectDateAsync(today, jumpToNow: true);
            return;
        }

        var nowX = (DateTimeOffset.Now - GetSelectedDayStart()).TotalMinutes *
            _pixelsPerMinute;
        SetHorizontalOffset(
            Math.Max(0, nowX - Math.Max(0, TimelineViewportWidth * 0.28)));
    }

    private void ApplyFilters()
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

        _visibleRows.Clear();
        _visibleRows.AddRange(query);
        _verticalOffset = 0;
        _targetVerticalOffset = 0;
        _realizedBandStart = -1;
        _realizedBandEnd = -1;

        var hasNoChannels = _visibleRows.Count == 0;
        var hasNoProgrammeData =
            !hasNoChannels &&
            _visibleRows.All(static row => row.Programmes.Count == 0);
        ProgrammeEmptyState.Visibility = hasNoChannels
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProgrammeNoScheduleState.Visibility = hasNoProgrammeData
            ? Visibility.Visible
            : Visibility.Collapsed;

        VisibleChannelCountText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0} каналов",
            _visibleRows.Count);
        ProgrammeSummaryText.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0} каналов · {1} с программой",
            _visibleRows.Count,
            _visibleRows.Count(static row => row.Programmes.Count > 0));
        UpdateScrollRanges();
        QueueViewportRender();
    }

    private async void PreviousDayButton_Click(object sender, RoutedEventArgs e) =>
        await SelectDateAsync(_selectedDate.AddDays(-1), jumpToNow: false);

    private async void NextDayButton_Click(object sender, RoutedEventArgs e) =>
        await SelectDateAsync(_selectedDate.AddDays(1), jumpToNow: false);

    private void JumpNowButton_Click(object sender, RoutedEventArgs e) =>
        JumpToNow();

    private async void ProgrammeSearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        _filterDebounceCancellation?.Cancel();
        _filterDebounceCancellation?.Dispose();
        _filterDebounceCancellation = new CancellationTokenSource();
        try
        {
            await Task.Delay(180, _filterDebounceCancellation.Token);
            ApplyFilters();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ProgrammeCategoryComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_updatingCategory)
        {
            ApplyFilters();
        }
    }

    private void ProgrammeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: EpgProgrammeBlockItem programme })
        {
            _keyboardProgramme = programme;
            ShowProgrammeDetails(programme);
        }
    }

    private void PlayProgrammeChannelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProgramme is not null)
        {
            PlayChannelRequested?.Invoke(
                this,
                new PlayChannelRequestedEventArgs(_selectedProgramme.ChannelStableId));
        }
    }

    private void ProgrammeRoot_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Home:
                JumpToNow();
                e.Handled = true;
                break;
            case VirtualKey.PageUp:
                SetHorizontalOffset(_horizontalOffset - TimelineViewportWidth * 0.8);
                e.Handled = true;
                break;
            case VirtualKey.PageDown:
                SetHorizontalOffset(_horizontalOffset + TimelineViewportWidth * 0.8);
                e.Handled = true;
                break;
            case VirtualKey.Escape when ProgrammeDetailsCard.Visibility == Visibility.Visible:
                ProgrammeDetailsCard.Visibility = Visibility.Collapsed;
                _selectedProgramme = null;
                e.Handled = true;
                break;
        }
    }

    private static string FormatZoom(double pixelsPerMinute) =>
        $"{pixelsPerMinute:0.0}×";

    private Brush ResolveBrush(string key)
    {
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        var dictionaryKey = ProgrammeRoot.ActualTheme == ElementTheme.Light
            ? "Light"
            : "Default";
        if (resources.ThemeDictionaries[dictionaryKey] is ResourceDictionary dictionary &&
            dictionary[key] is Brush themeBrush)
        {
            return themeBrush;
        }

        return resources[key] as Brush ??
            new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private readonly record struct ProgrammeVisualKey(
        string ChannelStableId,
        DateTimeOffset Start,
        string Title)
    {
        public static ProgrammeVisualKey From(EpgProgrammeBlockItem item) =>
            new(item.ChannelStableId, item.Programme.Start, item.Programme.Title);
    }
}
using System.Collections.ObjectModel;
using System.Globalization;
using Efiron.Application.Live;
using Efiron.Desktop.Presentation;
using Efiron.Domain.ProgrammeGuide;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Efiron.Desktop.Views;

public sealed partial class ProgrammeGuideView : UserControl
{
    private const double PixelsPerMinute = 2;
    private const double MinutesPerDay = 24 * 60;
    private const double TimelineWidth = PixelsPerMinute * MinutesPerDay;

    private readonly ObservableCollection<EpgChannelRowItem> _visibleRows = [];
    private readonly List<EpgChannelRowItem> _allRows = [];

    private LiveCatalogSnapshot? _catalog;
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private EpgProgrammeBlockItem? _selectedProgramme;
    private ScrollViewer? _channelVerticalScrollViewer;
    private ScrollViewer? _timelineVerticalScrollViewer;
    private DispatcherQueueTimer? _clockTimer;
    private bool _synchronizingVerticalScroll;
    private bool _updatingCategory;

    public ProgrammeGuideView()
    {
        InitializeComponent();
        BuildTimelineHeader();
        UpdateSelectedDateText();

        Loaded += ProgrammeGuideView_Loaded;
        Unloaded += ProgrammeGuideView_Unloaded;
        ProgrammeRoot.ActualThemeChanged += ProgrammeRoot_ActualThemeChanged;
    }

    public event EventHandler<PlayChannelRequestedEventArgs>? PlayChannelRequested;

    public void SetCatalog(LiveCatalogSnapshot catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        _catalog = catalog;
        _selectedProgramme = null;
        ProgrammeDetailsCard.Visibility = Visibility.Collapsed;
        PopulateCategories();
        RebuildRows();
        QueueJumpToNow();
    }

    public void FocusSearch() =>
        ProgrammeSearchTextBox.Focus(FocusState.Programmatic);

    private void ProgrammeGuideView_Loaded(object sender, RoutedEventArgs e)
    {
        _clockTimer ??= DispatcherQueue.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(30);
        _clockTimer.IsRepeating = true;
        _clockTimer.Tick -= ClockTimer_Tick;
        _clockTimer.Tick += ClockTimer_Tick;
        _clockTimer.Start();
        UpdateCurrentTimeMarker();
    }

    private void ProgrammeGuideView_Unloaded(object sender, RoutedEventArgs e) =>
        _clockTimer?.Stop();

    private void ProgrammeRoot_ActualThemeChanged(
        FrameworkElement sender,
        object args) =>
        BuildTimelineHeader();

    private void ClockTimer_Tick(DispatcherQueueTimer sender, object args) =>
        UpdateCurrentTimeMarker();

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
                         .OrderBy(static category => category, StringComparer.CurrentCultureIgnoreCase))
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

    private void RebuildRows()
    {
        _allRows.Clear();
        if (_catalog is null)
        {
            ApplyFilters();
            return;
        }

        var dayStart = GetSelectedDayStart();
        var dayEnd = dayStart.AddDays(1);
        var now = DateTimeOffset.Now;

        for (var index = 0; index < _catalog.Channels.Count; index++)
        {
            var channel = _catalog.Channels[index];
            var programmes = BuildProgrammeBlocks(
                channel,
                dayStart,
                dayEnd,
                now);
            _allRows.Add(new EpgChannelRowItem(
                index + 1,
                channel,
                programmes,
                TimelineWidth));
        }

        ApplyFilters();
        UpdateSelectedDateText();
        UpdateCurrentTimeMarker();
    }

    private static IReadOnlyList<EpgProgrammeBlockItem> BuildProgrammeBlocks(
        LiveChannelSnapshot channel,
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        DateTimeOffset now)
    {
        var schedule = channel.Schedule;
        if (schedule.Count == 0)
        {
            return Array.Empty<EpgProgrammeBlockItem>();
        }

        var blocks = new List<EpgProgrammeBlockItem>();
        for (var index = 0; index < schedule.Count; index++)
        {
            var programme = schedule[index];
            var effectiveStop = programme.Stop ??
                (index + 1 < schedule.Count
                    ? schedule[index + 1].Start
                    : programme.Start.AddMinutes(30));
            if (programme.Start >= dayEnd || effectiveStop <= dayStart)
            {
                continue;
            }

            var clippedStart = programme.Start < dayStart
                ? dayStart
                : programme.Start;
            var clippedStop = effectiveStop > dayEnd
                ? dayEnd
                : effectiveStop;
            var left = (clippedStart - dayStart).TotalMinutes * PixelsPerMinute;
            var width = Math.Max(
                4,
                (clippedStop - clippedStart).TotalMinutes * PixelsPerMinute);
            var localStart = programme.Start.ToLocalTime();
            var localStop = effectiveStop.ToLocalTime();
            var timeText = $"{localStart:HH:mm}–{localStop:HH:mm}";
            var isCurrent = programme.Start <= now && now < effectiveStop;

            blocks.Add(new EpgProgrammeBlockItem(
                channel.Channel.StableId,
                programme,
                left,
                width,
                timeText,
                isCurrent));
        }

        return blocks;
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
                row.Programmes.Any(block =>
                    block.Title.Contains(
                        search,
                        StringComparison.CurrentCultureIgnoreCase)));
        }

        var filtered = query.ToArray();
        _visibleRows.Clear();
        foreach (var row in filtered)
        {
            _visibleRows.Add(row);
        }

        ProgrammeEmptyState.Visibility = filtered.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChannelRowsListView.Visibility = filtered.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        TimelineViewportGrid.Visibility = filtered.Length == 0
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

    private void BuildTimelineHeader()
    {
        TimelineHeaderGrid.Children.Clear();
        TimelineHeaderGrid.ColumnDefinitions.Clear();
        TimelineHeaderGrid.Width = TimelineWidth;

        for (var slot = 0; slot < 48; slot++)
        {
            TimelineHeaderGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(60) });
            var minute = slot * 30;
            var label = TimeOnly.MinValue.AddMinutes(minute).ToString(
                "HH:mm",
                CultureInfo.CurrentCulture);
            var border = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 1),
                BorderBrush = ResolveBrush("EfironStrokeSubtleBrush"),
                Padding = new Thickness(9, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = label,
                    Style = ResolveStyle("EfironCaptionStyle"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Grid.SetColumn(border, slot);
            TimelineHeaderGrid.Children.Add(border);
        }
    }

    private static Brush ResolveBrush(string key) =>
        Microsoft.UI.Xaml.Application.Current.Resources[key] as Brush ??
        new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private static Style? ResolveStyle(string key) =>
        Microsoft.UI.Xaml.Application.Current.Resources[key] as Style;

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
        var value = _selectedDate.ToDateTime(TimeOnly.MinValue);
        SelectedDateText.Text = value.ToString("ddd, d MMMM", CultureInfo.CurrentCulture);
    }

    private void SelectDate(DateOnly date, bool jumpToNow)
    {
        _selectedDate = date;
        _selectedProgramme = null;
        ProgrammeDetailsCard.Visibility = Visibility.Collapsed;
        RebuildRows();
        if (jumpToNow)
        {
            QueueJumpToNow();
        }
        else
        {
            TimelineHorizontalScrollViewer.ChangeView(0, null, null, true);
        }
    }

    private void QueueJumpToNow() =>
        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            JumpToNow);

    private void JumpToNow()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (_selectedDate != today)
        {
            SelectDate(today, jumpToNow: true);
            return;
        }

        var now = DateTimeOffset.Now;
        var dayStart = GetSelectedDayStart();
        var nowX = (now - dayStart).TotalMinutes * PixelsPerMinute;
        var target = Math.Max(
            0,
            nowX - Math.Max(0, TimelineViewportGrid.ActualWidth * 0.28));
        TimelineHorizontalScrollViewer.ChangeView(target, null, null, false);
        UpdateCurrentTimeMarker();
    }

    private void UpdateCurrentTimeMarker()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (_selectedDate != today || TimelineViewportGrid.ActualWidth <= 0)
        {
            CurrentTimeLine.Visibility = Visibility.Collapsed;
            return;
        }

        var now = DateTimeOffset.Now;
        var nowX = (now - GetSelectedDayStart()).TotalMinutes * PixelsPerMinute;
        var viewportX = nowX - TimelineHorizontalScrollViewer.HorizontalOffset;
        var visible = viewportX >= 0 && viewportX <= TimelineViewportGrid.ActualWidth;
        CurrentTimeLine.Visibility = visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!visible)
        {
            return;
        }

        CurrentTimeLine.Height = TimelineViewportGrid.ActualHeight;
        Canvas.SetLeft(CurrentTimeLine, viewportX);
    }

    private void ChannelRowsListView_Loaded(object sender, RoutedEventArgs e)
    {
        _channelVerticalScrollViewer ??=
            FindDescendantScrollViewer(ChannelRowsListView);
        if (_channelVerticalScrollViewer is not null)
        {
            _channelVerticalScrollViewer.ViewChanged -= ChannelVerticalScrollViewer_ViewChanged;
            _channelVerticalScrollViewer.ViewChanged += ChannelVerticalScrollViewer_ViewChanged;
        }
    }

    private void TimelineRowsListView_Loaded(object sender, RoutedEventArgs e)
    {
        _timelineVerticalScrollViewer ??=
            FindDescendantScrollViewer(TimelineRowsListView);
        if (_timelineVerticalScrollViewer is not null)
        {
            _timelineVerticalScrollViewer.ViewChanged -= TimelineVerticalScrollViewer_ViewChanged;
            _timelineVerticalScrollViewer.ViewChanged += TimelineVerticalScrollViewer_ViewChanged;
        }
    }

    private void ChannelVerticalScrollViewer_ViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs e)
    {
        if (_synchronizingVerticalScroll ||
            sender is not ScrollViewer source ||
            _timelineVerticalScrollViewer is null)
        {
            return;
        }

        _synchronizingVerticalScroll = true;
        _timelineVerticalScrollViewer.ChangeView(
            null,
            source.VerticalOffset,
            null,
            true);
        _synchronizingVerticalScroll = false;
    }

    private void TimelineVerticalScrollViewer_ViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs e)
    {
        if (_synchronizingVerticalScroll ||
            sender is not ScrollViewer source ||
            _channelVerticalScrollViewer is null)
        {
            return;
        }

        _synchronizingVerticalScroll = true;
        _channelVerticalScrollViewer.ChangeView(
            null,
            source.VerticalOffset,
            null,
            true);
        _synchronizingVerticalScroll = false;
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            var nested = FindDescendantScrollViewer(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void TimelineHorizontalScrollViewer_ViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs e)
    {
        TimelineHeaderScrollViewer.ChangeView(
            TimelineHorizontalScrollViewer.HorizontalOffset,
            null,
            null,
            true);
        UpdateCurrentTimeMarker();
    }

    private void TimelineViewportGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateCurrentTimeMarker();

    private void ProgrammeRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        EpgChannelColumn.Width = new GridLength(e.NewSize.Width < 860 ? 196 : 252);
        ProgrammeSummaryText.Visibility = e.NewSize.Width < 760
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void PreviousDayButton_Click(object sender, RoutedEventArgs e) =>
        SelectDate(_selectedDate.AddDays(-1), jumpToNow: false);

    private void NextDayButton_Click(object sender, RoutedEventArgs e) =>
        SelectDate(_selectedDate.AddDays(1), jumpToNow: false);

    private void JumpNowButton_Click(object sender, RoutedEventArgs e) =>
        JumpToNow();

    private void ProgrammeSearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e) =>
        ApplyFilters();

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
        if (sender is not FrameworkElement { Tag: EpgProgrammeBlockItem programme })
        {
            return;
        }

        _selectedProgramme = programme;
        var channel = _catalog?.Channels.FirstOrDefault(snapshot => string.Equals(
            snapshot.Channel.StableId,
            programme.ChannelStableId,
            StringComparison.Ordinal));
        DetailsTimeText.Text = programme.TimeText;
        DetailsChannelText.Text = channel?.Channel.Name ?? string.Empty;
        DetailsTitleText.Text = programme.Title;
        DetailsDescriptionText.Text = string.IsNullOrWhiteSpace(programme.Description)
            ? "Описание передачи не предоставлено"
            : programme.Description;
        ProgrammeDetailsCard.Visibility = Visibility.Visible;
    }

    private void ChannelRowsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is EpgChannelRowItem row)
        {
            PlayChannelRequested?.Invoke(
                this,
                new PlayChannelRequestedEventArgs(row.StableId));
        }
    }

    private void PlayProgrammeChannelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProgramme is null)
        {
            return;
        }

        PlayChannelRequested?.Invoke(
            this,
            new PlayChannelRequestedEventArgs(_selectedProgramme.ChannelStableId));
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
                TimelineHorizontalScrollViewer.ChangeView(
                    Math.Max(
                        0,
                        TimelineHorizontalScrollViewer.HorizontalOffset -
                        TimelineViewportGrid.ActualWidth * 0.8),
                    null,
                    null,
                    false);
                e.Handled = true;
                break;
            case VirtualKey.PageDown:
                TimelineHorizontalScrollViewer.ChangeView(
                    TimelineHorizontalScrollViewer.HorizontalOffset +
                    TimelineViewportGrid.ActualWidth * 0.8,
                    null,
                    null,
                    false);
                e.Handled = true;
                break;
            case VirtualKey.Escape when ProgrammeDetailsCard.Visibility == Visibility.Visible:
                ProgrammeDetailsCard.Visibility = Visibility.Collapsed;
                _selectedProgramme = null;
                e.Handled = true;
                break;
        }
    }
}

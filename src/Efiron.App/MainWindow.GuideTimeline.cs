using System.Globalization;
using Efiron.App.Epg;
using Efiron.Core.Epg;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Efiron.App;

public sealed partial class MainWindow
{
    private const int TimelineChannelsPerRange = 50;
    private const double TimelineRowHeight = 56;
    private static readonly TimeSpan TimelineWindowDuration = TimeSpan.FromHours(6);
    private static readonly TimeSpan TimelineWindowStep = TimeSpan.FromHours(3);

    private readonly DispatcherTimer _guideTimelineClockTimer = new()
    {
        Interval = TimeSpan.FromSeconds(30),
    };

    private bool _guideTimelineInitialized;
    private bool _isGuideTimelineMode;
    private bool _isTimelineUpdatingDate;
    private bool _isRenderingGuideTimeline;
    private bool _isUpdatingTimelineRangeSelector;
    private bool _timelineFitResizePending;
    private int _timelinePageIndex;
    private int _timelinePendingScrollRow = -1;
    private int _timelineRangeSelectorChannelCount = -1;
    private double _timelinePixelsPerHour = 180;
    private double _lastFitViewportWidth;
    private DateTimeOffset _timelineWindowStart;

    private Grid _guideListWorkspace = null!;
    private Grid _guideTimelineWorkspace = null!;
    private Button _guideListModeButton = null!;
    private Button _guideTimelineModeButton = null!;
    private Button _timelinePreviousPageButton = null!;
    private Button _timelineNextPageButton = null!;
    private ComboBox _timelineRangeComboBox = null!;
    private TextBlock _timelineWindowText = null!;
    private TextBlock _timelinePageText = null!;
    private TextBlock _timelineEmptyText = null!;
    private StackPanel _timelineChannelLabels = null!;
    private StackPanel _timelineRows = null!;
    private Canvas _timelineHeaderCanvas = null!;
    private ScrollViewer _timelineHeaderScroll = null!;
    private ScrollViewer _timelineBodyHorizontalScroll = null!;
    private ScrollViewer _timelineVerticalScroll = null!;

    internal void InitializeGuideTimelineWorkspace()
    {
        if (_guideTimelineInitialized)
        {
            return;
        }

        _guideTimelineInitialized = true;
        _guideRefinementResources = new ResourceLoader(
            ResourceLoader.GetDefaultResourceFilePath(),
            "GuideRefinements");
        _timelineWindowStart = AlignTimelineToNow(DateTimeOffset.Now);

        _guideListWorkspace = GuideView.Children
            .OfType<Grid>()
            .First(child => Grid.GetRow(child) == 2);
        var selectorToolbar = GuideView.Children
            .OfType<Grid>()
            .First(child => Grid.GetRow(child) == 1);

        AddGuideModeButtons(selectorToolbar);
        AddTimelineWorkspace();

        _guideTimelineClockTimer.Tick += GuideTimelineClockTimer_Tick;
        _guideTimelineClockTimer.Start();
        LoadPlaylistButton.Click += GuideTimelineLoadPlaylistButton_Click;
        LoadEpgButton.Click += GuideTimelineLoadEpgButton_Click;
        GuideDatePicker.DateChanged += GuideTimelineDatePicker_DateChanged;
        GuideChannelComboBox.SelectionChanged += GuideTimelineChannelComboBox_SelectionChanged;
        RootNavigation.SelectionChanged += GuideTimelineRootNavigation_SelectionChanged;
        Closed += GuideTimelineWindow_Closed;

        SwitchGuideMode(timelineMode: false);
    }

    private void AddGuideModeButtons(Grid toolbar)
    {
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _guideListModeButton = new Button
        {
            Content = _resources.GetString("GuideListMode"),
            MinWidth = 92,
        };
        _guideListModeButton.Click += GuideListModeButton_Click;
        Grid.SetColumn(_guideListModeButton, toolbar.ColumnDefinitions.Count - 2);
        toolbar.Children.Add(_guideListModeButton);

        _guideTimelineModeButton = new Button
        {
            Content = _resources.GetString("GuideTimelineMode"),
            MinWidth = 92,
        };
        _guideTimelineModeButton.Click += GuideTimelineModeButton_Click;
        Grid.SetColumn(_guideTimelineModeButton, toolbar.ColumnDefinitions.Count - 1);
        toolbar.Children.Add(_guideTimelineModeButton);
    }

    private void AddTimelineWorkspace()
    {
        _guideTimelineWorkspace = new Grid
        {
            RowSpacing = 10,
            Visibility = Visibility.Collapsed,
        };
        _guideTimelineWorkspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _guideTimelineWorkspace.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        Grid.SetRow(_guideTimelineWorkspace, 2);

        var commandBar = CreateTimelineCommandBar();
        _guideTimelineWorkspace.Children.Add(commandBar);

        var timelineBody = CreateTimelineBody();
        Grid.SetRow(timelineBody, 1);
        _guideTimelineWorkspace.Children.Add(timelineBody);
        GuideView.Children.Add(_guideTimelineWorkspace);
    }

    private Grid CreateTimelineCommandBar()
    {
        var grid = new Grid { ColumnSpacing = 8 };
        for (var index = 0; index < 8; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = index == 3
                    ? new GridLength(1, GridUnitType.Star)
                    : GridLength.Auto,
            });
        }

        grid.Children.Add(CreateCommandButton(
            "‹ 3 h",
            _resources.GetString("GuideTimelinePreviousWindow"),
            (_, _) => MoveTimelineWindow(-TimelineWindowStep)));

        var nowButton = CreateCommandButton(
            _resources.GetString("GuideTimelineNow"),
            _resources.GetString("GuideTimelineNow"),
            TimelineNowButton_Click);
        Grid.SetColumn(nowButton, 1);
        grid.Children.Add(nowButton);

        var nextWindowButton = CreateCommandButton(
            "3 h ›",
            _resources.GetString("GuideTimelineNextWindow"),
            (_, _) => MoveTimelineWindow(TimelineWindowStep));
        Grid.SetColumn(nextWindowButton, 2);
        grid.Children.Add(nextWindowButton);

        _timelineWindowText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(_timelineWindowText, 3);
        grid.Children.Add(_timelineWindowText);

        _timelinePreviousPageButton = CreateCommandButton(
            "‹",
            _guideRefinementResources.GetString("PreviousRange"),
            TimelinePreviousPageButton_Click);
        Grid.SetColumn(_timelinePreviousPageButton, 4);
        grid.Children.Add(_timelinePreviousPageButton);

        _timelineRangeComboBox = new ComboBox
        {
            MinWidth = 108,
            DisplayMemberPath = nameof(TimelineRangeOption.Label),
        };
        _timelineRangeComboBox.SelectionChanged += TimelineRangeComboBox_SelectionChanged;
        ToolTipService.SetToolTip(
            _timelineRangeComboBox,
            _guideRefinementResources.GetString("RangeSelectorTooltip"));
        Grid.SetColumn(_timelineRangeComboBox, 5);
        grid.Children.Add(_timelineRangeComboBox);

        _timelinePageText = new TextBlock
        {
            MinWidth = 150,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_timelinePageText, 6);
        grid.Children.Add(_timelinePageText);

        _timelineNextPageButton = CreateCommandButton(
            "›",
            _guideRefinementResources.GetString("NextRange"),
            TimelineNextPageButton_Click);
        Grid.SetColumn(_timelineNextPageButton, 7);
        grid.Children.Add(_timelineNextPageButton);

        return grid;
    }

    private Border CreateTimelineBody()
    {
        var bodyGrid = new Grid();
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        bodyGrid.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        bodyGrid.Children.Add(new Border
        {
            Padding = new Thickness(12, 0, 8, 0),
            BorderBrush = NeutralBrush(42),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = new TextBlock
            {
                Text = _resources.GetString("GuideTimelineChannelHeader"),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });

        _timelineHeaderCanvas = new Canvas { Height = 38 };
        _timelineHeaderScroll = new ScrollViewer
        {
            Content = _timelineHeaderCanvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            IsHitTestVisible = false,
        };
        Grid.SetColumn(_timelineHeaderScroll, 1);
        bodyGrid.Children.Add(_timelineHeaderScroll);

        var rowsGrid = new Grid();
        rowsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        rowsGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        _timelineChannelLabels = new StackPanel();
        rowsGrid.Children.Add(_timelineChannelLabels);

        _timelineRows = new StackPanel();
        _timelineBodyHorizontalScroll = new ScrollViewer
        {
            Content = _timelineRows,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
        _timelineBodyHorizontalScroll.SizeChanged += TimelineBodyHorizontalScroll_SizeChanged;
        Grid.SetColumn(_timelineBodyHorizontalScroll, 1);
        rowsGrid.Children.Add(_timelineBodyHorizontalScroll);

        _timelineVerticalScroll = new ScrollViewer
        {
            Content = rowsGrid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
        };
        Grid.SetRow(_timelineVerticalScroll, 1);
        Grid.SetColumnSpan(_timelineVerticalScroll, 2);
        bodyGrid.Children.Add(_timelineVerticalScroll);

        _timelineEmptyText = new TextBlock
        {
            Text = _resources.GetString("GuideTimelineNoData"),
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        Grid.SetRow(_timelineEmptyText, 1);
        Grid.SetColumnSpan(_timelineEmptyText, 2);
        bodyGrid.Children.Add(_timelineEmptyText);

        return new Border
        {
            Background = NeutralBrush(18),
            BorderBrush = NeutralBrush(42),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(1),
            Child = bodyGrid,
        };
    }

    private static Button CreateCommandButton(
        string content,
        string tooltip,
        RoutedEventHandler handler)
    {
        var button = new Button { Content = content, MinWidth = 42 };
        button.Click += handler;
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static SolidColorBrush NeutralBrush(byte alpha) =>
        new(ColorHelper.FromArgb(alpha, 128, 128, 128));

    private void GuideListModeButton_Click(object sender, RoutedEventArgs e) =>
        SwitchGuideMode(timelineMode: false);

    private void GuideTimelineModeButton_Click(object sender, RoutedEventArgs e) =>
        SwitchGuideMode(timelineMode: true);

    private void SwitchGuideMode(bool timelineMode)
    {
        _isGuideTimelineMode = timelineMode;
        _guideListWorkspace.Visibility = timelineMode ? Visibility.Collapsed : Visibility.Visible;
        _guideTimelineWorkspace.Visibility = timelineMode ? Visibility.Visible : Visibility.Collapsed;
        _guideListModeButton.IsEnabled = timelineMode;
        _guideTimelineModeButton.IsEnabled = !timelineMode;

        if (timelineMode)
        {
            MoveTimelinePageToSelectedChannel();
            RenderGuideTimeline();
        }
    }

    private void TimelineNowButton_Click(object sender, RoutedEventArgs e)
    {
        _timelineWindowStart = AlignTimelineToNow(DateTimeOffset.Now);
        UpdateGuideDateFromTimeline();
        RenderGuideTimeline();
    }

    private void TimelinePreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_timelinePageIndex > 0)
        {
            _timelinePageIndex--;
            _timelinePendingScrollRow = 0;
            RenderGuideTimeline();
        }
    }

    private void TimelineNextPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_timelinePageIndex + 1 < GetTimelinePageCount())
        {
            _timelinePageIndex++;
            _timelinePendingScrollRow = 0;
            RenderGuideTimeline();
        }
    }

    private void TimelineRangeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingTimelineRangeSelector ||
            _timelineRangeComboBox.SelectedItem is not TimelineRangeOption option)
        {
            return;
        }

        _timelinePageIndex = option.PageIndex;
        _timelinePendingScrollRow = 0;
        RenderGuideTimeline();
    }

    private void MoveTimelineWindow(TimeSpan delta)
    {
        _timelineWindowStart = _timelineWindowStart.Add(delta);
        UpdateGuideDateFromTimeline();
        RenderGuideTimeline();
    }

    private void UpdateGuideDateFromTimeline()
    {
        _isTimelineUpdatingDate = true;
        SetGuideDate(DateOnly.FromDateTime(_timelineWindowStart.LocalDateTime));
        _isTimelineUpdatingDate = false;
    }

    private void GuideTimelineDatePicker_DateChanged(
        CalendarDatePicker sender,
        CalendarDatePickerDateChangedEventArgs args)
    {
        if (!_guideTimelineInitialized || _isTimelineUpdatingDate)
        {
            return;
        }

        var date = GetSelectedGuideDate();
        var time = TimeOnly.FromDateTime(_timelineWindowStart.LocalDateTime);
        _timelineWindowStart = CreateLocalInstant(date, time);
        RenderGuideTimeline();
    }

    private void GuideTimelineChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_guideTimelineInitialized && _isGuideTimelineMode)
        {
            MoveTimelinePageToSelectedChannel();
            RenderGuideTimeline();
        }
    }

    private async void GuideTimelineLoadPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        await WaitForButtonOperationAsync(LoadPlaylistButton);
        MoveTimelinePageToSelectedChannel();
        RenderGuideTimeline();
    }

    private async void GuideTimelineLoadEpgButton_Click(object sender, RoutedEventArgs e)
    {
        await WaitForButtonOperationAsync(LoadEpgButton);
        EnsureLiveScheduleIndex();
        MoveTimelinePageToSelectedChannel();
        RenderGuideTimeline();
    }

    private void GuideTimelineRootNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (_isGuideTimelineMode && args.SelectedItemContainer?.Tag is string tag && tag == "guide")
        {
            RenderGuideTimeline();
        }
    }

    private void GuideTimelineClockTimer_Tick(object? sender, object e)
    {
        if (_isGuideTimelineMode && GuideView.Visibility == Visibility.Visible)
        {
            RenderGuideTimeline();
        }
    }

    private void TimelineBodyHorizontalScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isGuideTimelineMode ||
            Math.Abs(e.NewSize.Width - _lastFitViewportWidth) < 2)
        {
            return;
        }

        _lastFitViewportWidth = e.NewSize.Width;
        if (_timelineFitResizePending)
        {
            return;
        }

        _timelineFitResizePending = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _timelineFitResizePending = false;
            RenderGuideTimeline();
        });
    }

    private void MoveTimelinePageToSelectedChannel()
    {
        var stableId = (GuideChannelComboBox.SelectedItem as EpgChannelListItem)?.Channel.StableId ??
            _selectedPlaylistChannelStableId;
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return;
        }

        var index = _guideChannels.FindIndex(item => item.Channel.StableId == stableId);
        if (index >= 0)
        {
            _timelinePageIndex = index / TimelineChannelsPerRange;
            _timelinePendingScrollRow = index % TimelineChannelsPerRange;
        }
    }

    private int GetTimelinePageCount() =>
        Math.Max(1, (int)Math.Ceiling(_guideChannels.Count / (double)TimelineChannelsPerRange));

    private void RenderGuideTimeline()
    {
        if (!_guideTimelineInitialized || !_isGuideTimelineMode || _isRenderingGuideTimeline)
        {
            return;
        }

        _isRenderingGuideTimeline = true;
        try
        {
            EnsureLiveScheduleIndex();

            var pageCount = GetTimelinePageCount();
            _timelinePageIndex = Math.Clamp(_timelinePageIndex, 0, pageCount - 1);
            _timelinePreviousPageButton.IsEnabled = _guideChannels.Count > 0 && _timelinePageIndex > 0;
            _timelineNextPageButton.IsEnabled =
                _guideChannels.Count > 0 && _timelinePageIndex + 1 < pageCount;
            PopulateTimelineRangeSelector(pageCount);

            var rangeStart = _guideChannels.Count == 0
                ? 0
                : (_timelinePageIndex * TimelineChannelsPerRange) + 1;
            var rangeEnd = Math.Min(
                _guideChannels.Count,
                (_timelinePageIndex + 1) * TimelineChannelsPerRange);
            _timelinePageText.Text = string.Format(
                CultureInfo.CurrentCulture,
                _guideRefinementResources.GetString("RangeFormat"),
                rangeStart,
                rangeEnd,
                _guideChannels.Count);

            var windowEnd = _timelineWindowStart.Add(TimelineWindowDuration);
            _timelineWindowText.Text = string.Format(
                CultureInfo.CurrentCulture,
                _resources.GetString("GuideTimelineWindowFormat"),
                _timelineWindowStart.ToLocalTime(),
                windowEnd.ToLocalTime());

            _timelineHeaderCanvas.Children.Clear();
            _timelineChannelLabels.Children.Clear();
            _timelineRows.Children.Clear();

            _timelinePixelsPerHour = ResolveTimelinePixelsPerHour();
            var timelineWidth = TimelineWindowDuration.TotalHours * _timelinePixelsPerHour;
            _timelineHeaderCanvas.Width = timelineWidth;
            RenderTimelineHeader(timelineWidth, windowEnd);

            var channels = _guideChannels
                .Skip(_timelinePageIndex * TimelineChannelsPerRange)
                .Take(TimelineChannelsPerRange)
                .ToArray();
            var hasData = _epgDocument is not null &&
                _liveScheduleIndex is not null &&
                channels.Length > 0;
            _timelineEmptyText.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
            if (!hasData)
            {
                return;
            }

            for (var index = 0; index < channels.Length; index++)
            {
                _timelineChannelLabels.Children.Add(CreateTimelineChannelButton(channels[index], index));
                _timelineRows.Children.Add(CreateTimelineRow(channels[index], index, timelineWidth, windowEnd));
            }

            QueuePendingTimelineScroll(channels.Length);
        }
        finally
        {
            _isRenderingGuideTimeline = false;
        }
    }

    private void PopulateTimelineRangeSelector(int pageCount)
    {
        if (_timelineRangeSelectorChannelCount != _guideChannels.Count)
        {
            _isUpdatingTimelineRangeSelector = true;
            _timelineRangeComboBox.Items.Clear();

            for (var pageIndex = 0; pageIndex < pageCount && _guideChannels.Count > 0; pageIndex++)
            {
                var start = (pageIndex * TimelineChannelsPerRange) + 1;
                var end = Math.Min(_guideChannels.Count, (pageIndex + 1) * TimelineChannelsPerRange);
                _timelineRangeComboBox.Items.Add(new TimelineRangeOption(
                    pageIndex,
                    string.Format(
                        CultureInfo.CurrentCulture,
                        _guideRefinementResources.GetString("RangeOptionFormat"),
                        start,
                        end)));
            }

            _timelineRangeSelectorChannelCount = _guideChannels.Count;
            _isUpdatingTimelineRangeSelector = false;
        }

        _isUpdatingTimelineRangeSelector = true;
        _timelineRangeComboBox.IsEnabled = _guideChannels.Count > 0;
        _timelineRangeComboBox.SelectedIndex = _guideChannels.Count == 0
            ? -1
            : _timelinePageIndex;
        _isUpdatingTimelineRangeSelector = false;
    }

    private double ResolveTimelinePixelsPerHour()
    {
        var viewportWidth = _timelineBodyHorizontalScroll.ActualWidth;
        if (viewportWidth < 320)
        {
            viewportWidth = 1080;
        }

        _lastFitViewportWidth = viewportWidth;
        return viewportWidth / TimelineWindowDuration.TotalHours;
    }

    private void QueuePendingTimelineScroll(int renderedChannelCount)
    {
        if (_timelinePendingScrollRow < 0 || renderedChannelCount <= 0)
        {
            return;
        }

        var row = Math.Clamp(_timelinePendingScrollRow, 0, renderedChannelCount - 1);
        _timelinePendingScrollRow = -1;
        DispatcherQueue.TryEnqueue(() =>
            _timelineVerticalScroll.ChangeView(
                horizontalOffset: null,
                verticalOffset: row * TimelineRowHeight,
                zoomFactor: null,
                disableAnimation: true));
    }

    private void RenderTimelineHeader(double timelineWidth, DateTimeOffset windowEnd)
    {
        var markerCount = (int)(TimelineWindowDuration.TotalMinutes / 30);
        for (var index = 0; index <= markerCount; index++)
        {
            var x = Math.Min(index * (_timelinePixelsPerHour / 2), timelineWidth - 1);
            var line = new Rectangle
            {
                Width = 1,
                Height = 38,
                Fill = NeutralBrush(index % 2 == 0 ? (byte)64 : (byte)32),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(line, x);
            _timelineHeaderCanvas.Children.Add(line);

            var label = new TextBlock
            {
                Text = _timelineWindowStart.AddMinutes(index * 30).ToLocalTime()
                    .ToString("t", CultureInfo.CurrentCulture),
                FontSize = 12,
                Opacity = index % 2 == 0 ? 0.82 : 0.58,
            };
            Canvas.SetLeft(label, Math.Min(x + 6, Math.Max(0, timelineWidth - 48)));
            Canvas.SetTop(label, 10);
            _timelineHeaderCanvas.Children.Add(label);
        }

        AddNowLine(_timelineHeaderCanvas, 38, windowEnd);
    }

    private Button CreateTimelineChannelButton(EpgChannelListItem channel, int rowIndex)
    {
        var button = new Button
        {
            Content = channel.Name,
            Tag = channel,
            Height = TimelineRowHeight,
            Padding = new Thickness(12, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = NeutralBrush(rowIndex % 2 == 0 ? (byte)18 : (byte)28),
        };
        button.Click += TimelineChannelButton_Click;
        ToolTipService.SetToolTip(button, channel.Name);
        return button;
    }

    private Canvas CreateTimelineRow(
        EpgChannelListItem channel,
        int rowIndex,
        double timelineWidth,
        DateTimeOffset windowEnd)
    {
        var canvas = new Canvas
        {
            Width = timelineWidth,
            Height = TimelineRowHeight,
            Background = NeutralBrush(rowIndex % 2 == 0 ? (byte)10 : (byte)20),
        };

        var markerCount = (int)(TimelineWindowDuration.TotalMinutes / 30);
        for (var index = 0; index <= markerCount; index++)
        {
            var line = new Rectangle
            {
                Width = 1,
                Height = TimelineRowHeight,
                Fill = NeutralBrush(index % 2 == 0 ? (byte)38 : (byte)22),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(line, Math.Min(index * (_timelinePixelsPerHour / 2), timelineWidth - 1));
            canvas.Children.Add(line);
        }

        foreach (var entry in _liveScheduleIndex!.FindRange(
                     channel.XmlTvChannelId,
                     _timelineWindowStart,
                     windowEnd))
        {
            canvas.Children.Add(CreateTimelineProgrammeButton(channel, entry));
        }

        AddNowLine(canvas, TimelineRowHeight, windowEnd);
        return canvas;
    }

    private Button CreateTimelineProgrammeButton(
        EpgChannelListItem channel,
        EpgTimelineEntry entry)
    {
        var left = (entry.VisibleStart - _timelineWindowStart).TotalHours * _timelinePixelsPerHour;
        var availableWidth =
            (entry.VisibleStop - entry.VisibleStart).TotalHours * _timelinePixelsPerHour - 3;
        var width = Math.Max(16, availableWidth);
        var title = GetProgrammeTitle(entry.Programme);
        var time = string.Format(
            CultureInfo.CurrentCulture,
            "{0:t}–{1:t}",
            entry.Programme.Start.ToLocalTime(),
            entry.EffectiveStop.ToLocalTime());

        var content = new StackPanel { Spacing = 1 };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        content.Children.Add(new TextBlock
        {
            Text = time,
            FontSize = 11,
            Opacity = 0.72,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var button = new Button
        {
            Content = content,
            Tag = new TimelineProgrammeSelection(channel, entry.Programme),
            Width = width,
            Height = 50,
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += TimelineProgrammeButton_Click;
        Canvas.SetLeft(button, left + 1);
        Canvas.SetTop(button, 3);

        ToolTipService.SetToolTip(button, string.Format(
            CultureInfo.CurrentCulture,
            _resources.GetString("GuideTimelineProgrammeTooltipFormat"),
            title,
            time,
            string.Join(" • ", entry.Programme.Categories)).TrimEnd());
        return button;
    }

    private void AddNowLine(Canvas canvas, double height, DateTimeOffset windowEnd)
    {
        var now = DateTimeOffset.Now;
        if (now < _timelineWindowStart || now >= windowEnd)
        {
            return;
        }

        var line = new Rectangle
        {
            Width = 2,
            Height = height,
            Fill = new SolidColorBrush(Colors.OrangeRed),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(line, (now - _timelineWindowStart).TotalHours * _timelinePixelsPerHour);
        Canvas.SetZIndex(line, 1000);
        canvas.Children.Add(line);
    }

    private void TimelineChannelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EpgChannelListItem channel })
        {
            SelectGuideChannelByStableId(channel.Channel.StableId);
            SwitchGuideMode(timelineMode: false);
        }
    }

    private void TimelineProgrammeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TimelineProgrammeSelection selection })
        {
            return;
        }

        SelectGuideChannelByStableId(selection.Channel.Channel.StableId);
        _isTimelineUpdatingDate = true;
        SetGuideDate(DateOnly.FromDateTime(selection.Programme.Start.ToLocalTime().DateTime));
        _isTimelineUpdatingDate = false;
        SwitchGuideMode(timelineMode: false);
        RefreshProgrammeList();

        if (ProgrammeListView.ItemsSource is IEnumerable<ProgrammeListItem> items)
        {
            var selected = items.FirstOrDefault(item =>
                item.Programme.ChannelId.Equals(selection.Programme.ChannelId, StringComparison.OrdinalIgnoreCase) &&
                item.Programme.Start == selection.Programme.Start &&
                string.Equals(item.Programme.Title, selection.Programme.Title, StringComparison.Ordinal));
            if (selected is not null)
            {
                ProgrammeListView.SelectedItem = selected;
                ProgrammeListView.ScrollIntoView(selected);
            }
        }
    }

    private static DateTimeOffset AlignTimelineToNow(DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        var aligned = new DateTimeOffset(
            local.Year,
            local.Month,
            local.Day,
            local.Hour,
            local.Minute < 30 ? 0 : 30,
            0,
            local.Offset);
        return aligned.AddHours(-1);
    }

    private static DateTimeOffset CreateLocalInstant(DateOnly date, TimeOnly time)
    {
        var localDateTime = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
    }

    private void GuideTimelineWindow_Closed(object sender, WindowEventArgs args)
    {
        _guideTimelineClockTimer.Stop();
        _guideTimelineClockTimer.Tick -= GuideTimelineClockTimer_Tick;
        LoadPlaylistButton.Click -= GuideTimelineLoadPlaylistButton_Click;
        LoadEpgButton.Click -= GuideTimelineLoadEpgButton_Click;
        GuideDatePicker.DateChanged -= GuideTimelineDatePicker_DateChanged;
        GuideChannelComboBox.SelectionChanged -= GuideTimelineChannelComboBox_SelectionChanged;
        RootNavigation.SelectionChanged -= GuideTimelineRootNavigation_SelectionChanged;
        _timelineBodyHorizontalScroll.SizeChanged -= TimelineBodyHorizontalScroll_SizeChanged;
        _timelineRangeComboBox.SelectionChanged -= TimelineRangeComboBox_SelectionChanged;
        _guideListModeButton.Click -= GuideListModeButton_Click;
        _guideTimelineModeButton.Click -= GuideTimelineModeButton_Click;
        Closed -= GuideTimelineWindow_Closed;
    }

    private sealed record TimelineProgrammeSelection(
        EpgChannelListItem Channel,
        XmlTvProgramme Programme);

    private sealed record TimelineRangeOption(int PageIndex, string Label);
}

using System.Globalization;
using Efiron.App.Epg;
using Efiron.Core.Epg;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Efiron.App;

public sealed partial class MainWindow
{
    private const int TimelineChannelsPerPage = 12;
    private static readonly TimeSpan TimelineWindowDuration = TimeSpan.FromHours(6);
    private static readonly TimeSpan TimelineWindowStep = TimeSpan.FromHours(3);

    private readonly DispatcherTimer _guideTimelineClockTimer = new()
    {
        Interval = TimeSpan.FromSeconds(30),
    };

    private bool _guideTimelineInitialized;
    private bool _isGuideTimelineMode;
    private bool _isTimelineUpdatingDate;
    private int _timelinePageIndex;
    private double _timelinePixelsPerHour = 180;
    private DateTimeOffset _timelineWindowStart;

    private Grid _guideListWorkspace = null!;
    private Grid _guideTimelineWorkspace = null!;
    private Button _guideListModeButton = null!;
    private Button _guideTimelineModeButton = null!;
    private Button _timelinePreviousWindowButton = null!;
    private Button _timelineNowButton = null!;
    private Button _timelineNextWindowButton = null!;
    private Button _timelinePreviousPageButton = null!;
    private Button _timelineNextPageButton = null!;
    private ComboBox _timelineZoomComboBox = null!;
    private TextBlock _timelineWindowText = null!;
    private TextBlock _timelinePageText = null!;
    private TextBlock _timelineEmptyText = null!;
    private StackPanel _timelineChannelLabels = null!;
    private StackPanel _timelineRows = null!;
    private Canvas _timelineHeaderCanvas = null!;
    private ScrollViewer _timelineHeaderScroll = null!;
    private ScrollViewer _timelineBodyHorizontalScroll = null!;

    internal void InitializeGuideTimelineWorkspace()
    {
        if (_guideTimelineInitialized)
        {
            return;
        }

        _guideTimelineInitialized = true;
        _timelineWindowStart = AlignTimelineToNow(DateTimeOffset.Now);

        _guideListWorkspace = GuideView.Children
            .OfType<Grid>()
            .First(child => Grid.GetRow(child) == 2);

        var selectorToolbar = GuideView.Children
            .OfType<Grid>()
            .First(child => Grid.GetRow(child) == 1);

        CreateGuideModeButtons(selectorToolbar);
        CreateTimelineWorkspace();

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

    private void CreateGuideModeButtons(Grid selectorToolbar)
    {
        selectorToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selectorToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _guideListModeButton = new Button
        {
            Content = _resources.GetString("GuideListMode"),
            MinWidth = 92,
        };
        _guideListModeButton.Click += GuideListModeButton_Click;
        Grid.SetColumn(_guideListModeButton, selectorToolbar.ColumnDefinitions.Count - 2);
        selectorToolbar.Children.Add(_guideListModeButton);

        _guideTimelineModeButton = new Button
        {
            Content = _resources.GetString("GuideTimelineMode"),
            MinWidth = 92,
        };
        _guideTimelineModeButton.Click += GuideTimelineModeButton_Click;
        Grid.SetColumn(_guideTimelineModeButton, selectorToolbar.ColumnDefinitions.Count - 1);
        selectorToolbar.Children.Add(_guideTimelineModeButton);
    }

    private void CreateTimelineWorkspace()
    {
        _guideTimelineWorkspace = new Grid
        {
            RowSpacing = 10,
            Visibility = Visibility.Collapsed,
        };
        _guideTimelineWorkspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _guideTimelineWorkspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_guideTimelineWorkspace, 2);

        _guideTimelineWorkspace.Children.Add(CreateTimelineCommandBar());
        var timelineBody = CreateTimelineBody();
        Grid.SetRow(timelineBody, 1);
        _guideTimelineWorkspace.Children.Add(timelineBody);
        GuideView.Children.Add(_guideTimelineWorkspace);
    }

    private UIElement CreateTimelineCommandBar()
    {
        var commandGrid = new Grid
        {
            ColumnSpacing = 8,
        };
        commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _timelinePreviousWindowButton = CreateCommandButton(
            "‹ 3 h",
            _resources.GetString("GuideTimelinePreviousWindow"),
            TimelinePreviousWindowButton_Click);
        commandGrid.Children.Add(_timelinePreviousWindowButton);

        _timelineNowButton = CreateCommandButton(
            _resources.GetString("GuideTimelineNow"),
            _resources.GetString("GuideTimelineNow"),
            TimelineNowButton_Click);
        Grid.SetColumn(_timelineNowButton, 1);
        commandGrid.Children.Add(_timelineNowButton);

        _timelineNextWindowButton = CreateCommandButton(
            "3 h ›",
            _resources.GetString("GuideTimelineNextWindow"),
            TimelineNextWindowButton_Click);
        Grid.SetColumn(_timelineNextWindowButton, 2);
        commandGrid.Children.Add(_timelineNextWindowButton);

        _timelineWindowText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(_timelineWindowText, 3);
        commandGrid.Children.Add(_timelineWindowText);

        var zoomLabel = new TextBlock
        {
            Text = _resources.GetString("GuideTimelineZoom"),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.72,
        };
        Grid.SetColumn(zoomLabel, 4);
        commandGrid.Children.Add(zoomLabel);

        _timelineZoomComboBox = new ComboBox
        {
            MinWidth = 112,
            SelectedIndex = 1,
        };
        _timelineZoomComboBox.Items.Add(CreateZoomItem("67%", 120d));
        _timelineZoomComboBox.Items.Add(CreateZoomItem("100%", 180d));
        _timelineZoomComboBox.Items.Add(CreateZoomItem("133%", 240d));
        _timelineZoomComboBox.SelectionChanged += TimelineZoomComboBox_SelectionChanged;
        Grid.SetColumn(_timelineZoomComboBox, 5);
        commandGrid.Children.Add(_timelineZoomComboBox);

        _timelinePreviousPageButton = CreateCommandButton(
            "‹",
            _resources.GetString("GuideTimelinePreviousPage"),
            TimelinePreviousPageButton_Click);
        Grid.SetColumn(_timelinePreviousPageButton, 6);
        commandGrid.Children.Add(_timelinePreviousPageButton);

        _timelinePageText = new TextBlock
        {
            MinWidth = 112,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(_timelinePageText, 7);
        commandGrid.Children.Add(_timelinePageText);

        _timelineNextPageButton = CreateCommandButton(
            "›",
            _resources.GetString("GuideTimelineNextPage"),
            TimelineNextPageButton_Click);
        commandGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_timelineNextPageButton, 8);
        commandGrid.Children.Add(_timelineNextPageButton);

        return commandGrid;
    }

    private static Button CreateCommandButton(
        string content,
        string tooltip,
        RoutedEventHandler clickHandler)
    {
        var button = new Button
        {
            Content = content,
            MinWidth = 42,
        };
        button.Click += clickHandler;
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static ComboBoxItem CreateZoomItem(string label, double pixelsPerHour) =>
        new()
        {
            Content = label,
            Tag = pixelsPerHour,
        };

    private UIElement CreateTimelineBody()
    {
        var outerBorder = new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(18, 128, 128, 128)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(42, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(1),
        };

        var bodyGrid = new Grid();
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var channelHeader = new Border
        {
            Padding = new Thickness(12, 0, 8, 0),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(42, 128, 128, 128)),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = new TextBlock
            {
                Text = _resources.GetString("GuideTimelineChannelHeader"),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        bodyGrid.Children.Add(channelHeader);

        _timelineHeaderCanvas = new Canvas
        {
            Height = 38,
        };
        _timelineHeaderScroll = new ScrollViewer
        {
            Content = _timelineHeaderCanvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            IsHitTestVisible = false,
        };
        Grid.SetColumn(_timelineHeaderScroll, 1);
        bodyGrid.Children.Add(_timelineHeaderScroll);

        var verticalScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
        };
        Grid.SetRow(verticalScroll, 1);
        Grid.SetColumnSpan(verticalScroll, 2);

        var rowsGrid = new Grid();
        rowsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        rowsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _timelineChannelLabels = new StackPanel();
        rowsGrid.Children.Add(_timelineChannelLabels);

        _timelineRows = new StackPanel();
        _timelineBodyHorizontalScroll = new ScrollViewer
        {
            Content = _timelineRows,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
        _timelineBodyHorizontalScroll.ViewChanged += TimelineBodyHorizontalScroll_ViewChanged;
        Grid.SetColumn(_timelineBodyHorizontalScroll, 1);
        rowsGrid.Children.Add(_timelineBodyHorizontalScroll);

        verticalScroll.Content = rowsGrid;
        bodyGrid.Children.Add(verticalScroll);

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

        outerBorder.Child = bodyGrid;
        return outerBorder;
    }

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

    private void TimelinePreviousWindowButton_Click(object sender, RoutedEventArgs e) =>
        MoveTimelineWindow(-TimelineWindowStep);

    private void TimelineNextWindowButton_Click(object sender, RoutedEventArgs e) =>
        MoveTimelineWindow(TimelineWindowStep);

    private void TimelineNowButton_Click(object sender, RoutedEventArgs e)
    {
        _timelineWindowStart = AlignTimelineToNow(DateTimeOffset.Now);
        UpdateGuideDateFromTimeline();
        RenderGuideTimeline();
    }

    private void TimelinePreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_timelinePageIndex <= 0)
        {
            return;
        }

        _timelinePageIndex--;
        RenderGuideTimeline();
    }

    private void TimelineNextPageButton_Click(object sender, RoutedEventArgs e)
    {
        var pageCount = GetTimelinePageCount();
        if (_timelinePageIndex + 1 >= pageCount)
        {
            return;
        }

        _timelinePageIndex++;
        RenderGuideTimeline();
    }

    private void TimelineZoomComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_timelineZoomComboBox.SelectedItem is ComboBoxItem { Tag: double pixelsPerHour })
        {
            _timelinePixelsPerHour = pixelsPerHour;
            RenderGuideTimeline();
        }
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

        var selectedDate = GetSelectedGuideDate();
        var localTime = TimeOnly.FromDateTime(_timelineWindowStart.LocalDateTime);
        _timelineWindowStart = CreateLocalInstant(selectedDate, localTime);

        if (_isGuideTimelineMode)
        {
            RenderGuideTimeline();
        }
    }

    private void GuideTimelineChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_guideTimelineInitialized || !_isGuideTimelineMode)
        {
            return;
        }

        MoveTimelinePageToSelectedChannel();
        RenderGuideTimeline();
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

    private void TimelineBodyHorizontalScroll_ViewChanged(
        object? sender,
        ScrollViewerViewChangedEventArgs e)
    {
        _timelineHeaderScroll.ChangeView(
            _timelineBodyHorizontalScroll.HorizontalOffset,
            null,
            null,
            disableAnimation: true);
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
            _timelinePageIndex = index / TimelineChannelsPerPage;
        }
    }

    private int GetTimelinePageCount() =>
        Math.Max(1, (int)Math.Ceiling(_guideChannels.Count / (double)TimelineChannelsPerPage));

    private void RenderGuideTimeline()
    {
        if (!_guideTimelineInitialized || !_isGuideTimelineMode)
        {
            return;
        }

        EnsureLiveScheduleIndex();
        var pageCount = GetTimelinePageCount();
        _timelinePageIndex = Math.Clamp(_timelinePageIndex, 0, pageCount - 1);
        _timelinePreviousPageButton.IsEnabled = _timelinePageIndex > 0;
        _timelineNextPageButton.IsEnabled = _timelinePageIndex + 1 < pageCount;
        _timelinePageText.Text = string.Format(
            CultureInfo.CurrentCulture,
            _resources.GetString("GuideTimelinePageFormat"),
            _timelinePageIndex + 1,
            pageCount);

        var windowEnd = _timelineWindowStart.Add(TimelineWindowDuration);
        _timelineWindowText.Text = string.Format(
            CultureInfo.CurrentCulture,
            _resources.GetString("GuideTimelineWindowFormat"),
            _timelineWindowStart.ToLocalTime(),
            windowEnd.ToLocalTime());

        _timelineHeaderCanvas.Children.Clear();
        _timelineChannelLabels.Children.Clear();
        _timelineRows.Children.Clear();

        var timelineWidth = TimelineWindowDuration.TotalHours * _timelinePixelsPerHour;
        _timelineHeaderCanvas.Width = timelineWidth;
        RenderTimelineHeader(timelineWidth, windowEnd);

        var pageChannels = _guideChannels
            .Skip(_timelinePageIndex * TimelineChannelsPerPage)
            .Take(TimelineChannelsPerPage)
            .ToArray();

        var hasData = _epgDocument is not null &&
            _liveScheduleIndex is not null &&
            pageChannels.Length > 0;
        _timelineEmptyText.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
        if (!hasData)
        {
            return;
        }

        for (var rowIndex = 0; rowIndex < pageChannels.Length; rowIndex++)
        {
            var channel = pageChannels[rowIndex];
            _timelineChannelLabels.Children.Add(CreateTimelineChannelButton(channel, rowIndex));
            _timelineRows.Children.Add(CreateTimelineRow(channel, rowIndex, timelineWidth, windowEnd));
        }
    }

    private void RenderTimelineHeader(double timelineWidth, DateTimeOffset windowEnd)
    {
        var halfHourCount = (int)(TimelineWindowDuration.TotalMinutes / 30);
        for (var index = 0; index <= halfHourCount; index++)
        {
            var x = index * (_timelinePixelsPerHour / 2);
            var line = new Rectangle
            {
                Width = 1,
                Height = 38,
                Fill = new SolidColorBrush(ColorHelper.FromArgb(
                    index % 2 == 0 ? (byte)64 : (byte)32,
                    128,
                    128,
                    128)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(line, Math.Min(x, timelineWidth - 1));
            _timelineHeaderCanvas.Children.Add(line);

            var instant = _timelineWindowStart.AddMinutes(index * 30).ToLocalTime();
            var label = new TextBlock
            {
                Text = instant.ToString("t", CultureInfo.CurrentCulture),
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
            Height = 56,
            Padding = new Thickness(12, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontWeight = FontWeights.SemiBold,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = new SolidColorBrush(ColorHelper.FromArgb(
                rowIndex % 2 == 0 ? (byte)18 : (byte)28,
                128,
                128,
                128)),
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
            Height = 56,
            Background = new SolidColorBrush(ColorHelper.FromArgb(
                rowIndex % 2 == 0 ? (byte)10 : (byte)20,
                128,
                128,
                128)),
        };

        var halfHourCount = (int)(TimelineWindowDuration.TotalMinutes / 30);
        for (var index = 0; index <= halfHourCount; index++)
        {
            var line = new Rectangle
            {
                Width = 1,
                Height = 56,
                Fill = new SolidColorBrush(ColorHelper.FromArgb(
                    index % 2 == 0 ? (byte)38 : (byte)22,
                    128,
                    128,
                    128)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(line, Math.Min(index * (_timelinePixelsPerHour / 2), timelineWidth - 1));
            canvas.Children.Add(line);
        }

        var entries = _liveScheduleIndex!.FindRange(
            channel.XmlTvChannelId,
            _timelineWindowStart,
            windowEnd);
        foreach (var entry in entries)
        {
            canvas.Children.Add(CreateTimelineProgrammeButton(channel, entry));
        }

        AddNowLine(canvas, 56, windowEnd);
        return canvas;
    }

    private Button CreateTimelineProgrammeButton(
        EpgChannelListItem channel,
        EpgTimelineEntry entry)
    {
        var left = (entry.VisibleStart - _timelineWindowStart).TotalHours * _timelinePixelsPerHour;
        var width = Math.Max(
            28,
            (entry.VisibleStop - entry.VisibleStart).TotalHours * _timelinePixelsPerHour - 3);

        var localStart = entry.Programme.Start.ToLocalTime();
        var localStop = entry.EffectiveStop.ToLocalTime();
        var title = GetProgrammeTitle(entry.Programme);
        var time = string.Format(
            CultureInfo.CurrentCulture,
            "{0:t}–{1:t}",
            localStart,
            localStop);

        var content = new StackPanel
        {
            Spacing = 1,
        };
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

        var categories = string.Join(" • ", entry.Programme.Categories);
        var tooltip = string.Format(
            CultureInfo.CurrentCulture,
            _resources.GetString("GuideTimelineProgrammeTooltipFormat"),
            title,
            time,
            categories);
        ToolTipService.SetToolTip(button, tooltip.TrimEnd());
        return button;
    }

    private void AddNowLine(Canvas canvas, double height, DateTimeOffset windowEnd)
    {
        var now = DateTimeOffset.Now;
        if (now < _timelineWindowStart || now >= windowEnd)
        {
            return;
        }

        var x = (now - _timelineWindowStart).TotalHours * _timelinePixelsPerHour;
        var line = new Rectangle
        {
            Width = 2,
            Height = height,
            Fill = new SolidColorBrush(Colors.OrangeRed),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(line, x);
        Canvas.SetZIndex(line, 1000);
        canvas.Children.Add(line);
    }

    private void TimelineChannelButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EpgChannelListItem channel })
        {
            return;
        }

        SelectGuideChannelByStableId(channel.Channel.StableId);
        SwitchGuideMode(timelineMode: false);
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
        var alignedMinute = local.Minute < 30 ? 0 : 30;
        var aligned = new DateTimeOffset(
            local.Year,
            local.Month,
            local.Day,
            local.Hour,
            alignedMinute,
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
        _timelineBodyHorizontalScroll.ViewChanged -= TimelineBodyHorizontalScroll_ViewChanged;
        _guideListModeButton.Click -= GuideListModeButton_Click;
        _guideTimelineModeButton.Click -= GuideTimelineModeButton_Click;
        Closed -= GuideTimelineWindow_Closed;
    }

    private sealed record TimelineProgrammeSelection(
        EpgChannelListItem Channel,
        XmlTvProgramme Programme);
}

using System.Globalization;
using Efiron.App.Epg;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Efiron.App;

public sealed partial class MainWindow
{
    private const string TimelineNoCategoryTag = "\u0001NO_CATEGORY";

    private readonly List<EpgChannelListItem> _allTimelineGuideChannels = [];

    private bool _guideTimelineRefinementsInitialized;
    private bool _isUpdatingTimelineCategoryFilter;

    private ResourceLoader _guideRefinementResources = null!;
    private TextBox _timelineChannelSearchTextBox = null!;
    private ComboBox _timelineCategoryComboBox = null!;
    private TextBlock _timelineFilterSummaryText = null!;

    internal void InitializeGuideTimelineRefinements()
    {
        if (_guideTimelineRefinementsInitialized)
        {
            return;
        }

        _guideTimelineRefinementsInitialized = true;
        _guideRefinementResources ??= new ResourceLoader(
            ResourceLoader.GetDefaultResourceFilePath(),
            "GuideRefinements");

        AddTimelineFilterBar();
        CaptureAllTimelineGuideChannels();

        LoadPlaylistButton.Click += TimelineRefinementsLoadPlaylistButton_Click;
        LoadEpgButton.Click += TimelineRefinementsLoadEpgButton_Click;
        Closed += TimelineRefinementsWindow_Closed;
    }

    private void AddTimelineFilterBar()
    {
        var timelineBody = _guideTimelineWorkspace.Children
            .OfType<FrameworkElement>()
            .First(child => Grid.GetRow(child) == 1);

        _guideTimelineWorkspace.RowDefinitions.Insert(1, new RowDefinition
        {
            Height = GridLength.Auto,
        });
        Grid.SetRow(timelineBody, 2);

        var filterGrid = new Grid
        {
            ColumnSpacing = 8,
        };
        filterGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(2, GridUnitType.Star),
        });
        filterGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        filterGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        _timelineChannelSearchTextBox = new TextBox
        {
            PlaceholderText = _guideRefinementResources.GetString("SearchPlaceholder"),
        };
        _timelineChannelSearchTextBox.TextChanged += TimelineChannelSearchTextBox_TextChanged;
        filterGrid.Children.Add(_timelineChannelSearchTextBox);

        _timelineCategoryComboBox = new ComboBox
        {
            Header = _guideRefinementResources.GetString("CategoryHeader"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _timelineCategoryComboBox.SelectionChanged += TimelineCategoryComboBox_SelectionChanged;
        Grid.SetColumn(_timelineCategoryComboBox, 1);
        filterGrid.Children.Add(_timelineCategoryComboBox);

        _timelineFilterSummaryText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.72,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(_timelineFilterSummaryText, 2);
        filterGrid.Children.Add(_timelineFilterSummaryText);

        Grid.SetRow(filterGrid, 1);
        _guideTimelineWorkspace.Children.Add(filterGrid);
    }

    private void TimelineChannelSearchTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyTimelineChannelFilters(resetRange: true);

    private void TimelineCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUpdatingTimelineCategoryFilter)
        {
            ApplyTimelineChannelFilters(resetRange: true);
        }
    }

    private async void TimelineRefinementsLoadPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        var previousMatch = _epgMatchResult;
        await Task.Yield();
        await WaitForButtonOperationAsync(LoadPlaylistButton);

        if (!ReferenceEquals(previousMatch, _epgMatchResult))
        {
            CaptureAllTimelineGuideChannels();
        }
    }

    private async void TimelineRefinementsLoadEpgButton_Click(object sender, RoutedEventArgs e)
    {
        var previousDocument = _epgDocument;
        await Task.Yield();
        await WaitForButtonOperationAsync(LoadEpgButton);

        if (!ReferenceEquals(previousDocument, _epgDocument))
        {
            CaptureAllTimelineGuideChannels();
        }
    }

    private void CaptureAllTimelineGuideChannels()
    {
        _allTimelineGuideChannels.Clear();
        _allTimelineGuideChannels.AddRange(_guideChannels);
        PopulateTimelineCategoryFilter();
        ApplyTimelineChannelFilters(resetRange: true);
    }

    private void PopulateTimelineCategoryFilter()
    {
        var selectedTag = (_timelineCategoryComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        _isUpdatingTimelineCategoryFilter = true;
        _timelineCategoryComboBox.Items.Clear();
        _timelineCategoryComboBox.Items.Add(new ComboBoxItem
        {
            Content = _guideRefinementResources.GetString("AllCategories"),
        });

        foreach (var category in _allTimelineGuideChannels
                     .Select(item => item.Channel.GroupName)
                     .Where(static category => !string.IsNullOrWhiteSpace(category))
                     .Select(static category => category!)
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(static category => category, StringComparer.CurrentCultureIgnoreCase))
        {
            _timelineCategoryComboBox.Items.Add(new ComboBoxItem
            {
                Content = category,
                Tag = category,
            });
        }

        if (_allTimelineGuideChannels.Any(item => string.IsNullOrWhiteSpace(item.Channel.GroupName)))
        {
            _timelineCategoryComboBox.Items.Add(new ComboBoxItem
            {
                Content = _guideRefinementResources.GetString("NoCategory"),
                Tag = TimelineNoCategoryTag,
            });
        }

        var selectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(selectedTag))
        {
            for (var index = 1; index < _timelineCategoryComboBox.Items.Count; index++)
            {
                if ((_timelineCategoryComboBox.Items[index] as ComboBoxItem)?.Tag is string tag &&
                    string.Equals(tag, selectedTag, StringComparison.CurrentCultureIgnoreCase))
                {
                    selectedIndex = index;
                    break;
                }
            }
        }

        _timelineCategoryComboBox.SelectedIndex = selectedIndex;
        _isUpdatingTimelineCategoryFilter = false;
    }

    private void ApplyTimelineChannelFilters(bool resetRange)
    {
        if (!_guideTimelineRefinementsInitialized)
        {
            return;
        }

        var preferredStableId =
            (GuideChannelComboBox.SelectedItem as EpgChannelListItem)?.Channel.StableId ??
            _selectedPlaylistChannelStableId;
        var search = _timelineChannelSearchTextBox.Text?.Trim();
        var categoryTag = (_timelineCategoryComboBox.SelectedItem as ComboBoxItem)?.Tag as string;

        IEnumerable<EpgChannelListItem> query = _allTimelineGuideChannels;
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(item =>
                item.Channel.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                (item.Channel.TvgName?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (item.Channel.TvgId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (categoryTag == TimelineNoCategoryTag)
        {
            query = query.Where(item => string.IsNullOrWhiteSpace(item.Channel.GroupName));
        }
        else if (!string.IsNullOrWhiteSpace(categoryTag))
        {
            query = query.Where(item => string.Equals(
                item.Channel.GroupName,
                categoryTag,
                StringComparison.CurrentCultureIgnoreCase));
        }

        var filtered = query.ToList();
        var timelineMode = _isGuideTimelineMode;
        _isGuideTimelineMode = false;
        _isUpdatingGuideChannel = true;
        _guideChannels.Clear();
        _guideChannels.AddRange(filtered);
        GuideChannelComboBox.ItemsSource = null;
        GuideChannelComboBox.ItemsSource = _guideChannels;
        GuideChannelComboBox.SelectedIndex = FindGuideChannelIndex(preferredStableId);
        _isUpdatingGuideChannel = false;
        _isGuideTimelineMode = timelineMode;

        _timelineRangeSelectorChannelCount = -1;
        if (resetRange)
        {
            _timelinePageIndex = 0;
            _timelinePendingScrollRow = 0;
        }

        MoveTimelinePageToSelectedChannel();
        _timelineFilterSummaryText.Text = string.Format(
            CultureInfo.CurrentCulture,
            _guideRefinementResources.GetString("SummaryFormat"),
            _guideChannels.Count,
            _allTimelineGuideChannels.Count);
        _timelineEmptyText.Text = _guideChannels.Count == 0 && _allTimelineGuideChannels.Count > 0
            ? _guideRefinementResources.GetString("NoChannels")
            : _resources.GetString("GuideTimelineNoData");

        if (timelineMode)
        {
            RenderGuideTimeline();
        }
        else
        {
            RefreshProgrammeList();
        }
    }

    private void TimelineRefinementsWindow_Closed(object sender, WindowEventArgs args)
    {
        LoadPlaylistButton.Click -= TimelineRefinementsLoadPlaylistButton_Click;
        LoadEpgButton.Click -= TimelineRefinementsLoadEpgButton_Click;
        _timelineChannelSearchTextBox.TextChanged -= TimelineChannelSearchTextBox_TextChanged;
        _timelineCategoryComboBox.SelectionChanged -= TimelineCategoryComboBox_SelectionChanged;
        Closed -= TimelineRefinementsWindow_Closed;
    }
}

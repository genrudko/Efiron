using System.Globalization;
using Efiron.App.Channels;
using Efiron.App.Epg;
using Efiron.App.Playlists;
using Efiron.Core.Channels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Efiron.App;

public sealed partial class MainWindow
{
    private const string FavoritesGroupTag = "\u0001FAVORITES";
    private const string NoCategoryGroupTag = "\u0001NO_CATEGORY";

    private readonly ChannelCatalogService _channelCatalogService = new();

    private ChannelLibrarySnapshot _channelLibrarySnapshot = ChannelLibrarySnapshot.Empty;
    private IReadOnlyList<ChannelPresentation> _channelCatalog = [];
    private IReadOnlyList<ChannelPresentation> _favoriteChannelCatalog = [];
    private ResourceLoader _channelLibraryResources = null!;
    private ComboBox _channelNumberingComboBox = null!;
    private Button _channelFavoriteButton = null!;
    private Button _channelEditButton = null!;
    private CheckBox _showHiddenChannelsCheckBox = null!;
    private bool _channelLibraryInitialized;
    private bool _isUpdatingChannelLibraryControls;

    internal void InitializeChannelLibraryWorkspace()
    {
        if (_channelLibraryInitialized)
        {
            return;
        }

        _channelLibraryInitialized = true;
        _channelLibraryResources = new ResourceLoader(
            ResourceLoader.GetDefaultResourceFilePath(),
            "ChannelLibrary");
        _channelLibrarySnapshot = ChannelCustomizationStore.Load(out var invalidStoreRecovered);

        CreateChannelLibraryCompatibilityControls();
        SetSelectedNumberingMode();

        LoadPlaylistButton.Click += ChannelLibraryLoadPlaylistButton_Click;
        LoadEpgButton.Click += ChannelLibraryLoadEpgButton_Click;
        ChannelSearchTextBox.TextChanged += ChannelLibrarySearchTextBox_TextChanged;
        GroupFilterComboBox.SelectionChanged += ChannelLibraryGroupFilterComboBox_SelectionChanged;
        ChannelListView.SelectionChanged += ChannelLibraryChannelListView_SelectionChanged;
        Closed += ChannelLibraryWindow_Closed;

        RebuildChannelCatalog(rebuildGuide: true);

        if (invalidStoreRecovered)
        {
            LiveScreen.ShowMessage(
                InfoBarSeverity.Warning,
                _channelLibraryResources.GetString("StoreRecoveredTitle"),
                _channelLibraryResources.GetString("StoreRecoveredMessage"));
        }
    }

    private void CreateChannelLibraryCompatibilityControls()
    {
        _channelNumberingComboBox = new ComboBox { Visibility = Visibility.Collapsed };
        AddNumberingItem(ChannelNumberingMode.ProviderOrder, "NumberingProvider");
        AddNumberingItem(ChannelNumberingMode.Continuous, "NumberingContinuous");
        AddNumberingItem(ChannelNumberingMode.PerCategory, "NumberingPerCategory");
        AddNumberingItem(ChannelNumberingMode.Manual, "NumberingManual");
        _channelNumberingComboBox.SelectionChanged += ChannelNumberingComboBox_SelectionChanged;

        _channelFavoriteButton = new Button
        {
            Visibility = Visibility.Collapsed,
            IsEnabled = false,
        };
        _channelFavoriteButton.Click += ChannelFavoriteButton_Click;

        _channelEditButton = new Button
        {
            Visibility = Visibility.Collapsed,
            IsEnabled = false,
        };
        _channelEditButton.Click += ChannelEditButton_Click;

        _showHiddenChannelsCheckBox = new CheckBox
        {
            Visibility = Visibility.Collapsed,
            IsChecked = false,
        };
        _showHiddenChannelsCheckBox.Checked += ShowHiddenChannelsCheckBox_Changed;
        _showHiddenChannelsCheckBox.Unchecked += ShowHiddenChannelsCheckBox_Changed;

        CompatibilityBridge.Children.Add(_channelNumberingComboBox);
        CompatibilityBridge.Children.Add(_channelFavoriteButton);
        CompatibilityBridge.Children.Add(_channelEditButton);
        CompatibilityBridge.Children.Add(_showHiddenChannelsCheckBox);
    }

    private void AddNumberingItem(ChannelNumberingMode mode, string resourceKey) =>
        _channelNumberingComboBox.Items.Add(new ComboBoxItem
        {
            Content = _channelLibraryResources.GetString(resourceKey),
            Tag = mode,
        });

    private void SetSelectedNumberingMode()
    {
        _isUpdatingChannelLibraryControls = true;
        for (var index = 0; index < _channelNumberingComboBox.Items.Count; index++)
        {
            if (_channelNumberingComboBox.Items[index] is ComboBoxItem { Tag: ChannelNumberingMode mode } &&
                mode == _channelLibrarySnapshot.Settings.NumberingMode)
            {
                _channelNumberingComboBox.SelectedIndex = index;
                break;
            }
        }

        _isUpdatingChannelLibraryControls = false;
    }

    private async void ChannelLibraryLoadPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        await Task.Yield();
        await WaitForButtonOperationAsync(LoadPlaylistButton);
        DispatcherQueue.TryEnqueue(() => RebuildChannelCatalog(rebuildGuide: true));
    }

    private async void ChannelLibraryLoadEpgButton_Click(object sender, RoutedEventArgs e)
    {
        await Task.Yield();
        await WaitForButtonOperationAsync(LoadEpgButton);
        DispatcherQueue.TryEnqueue(() =>
        {
            RebuildGuideChannelsFromCatalog();
            ApplyChannelLibraryFilter();
        });
    }

    private void ChannelLibrarySearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUpdatingChannelLibraryControls)
        {
            ApplyChannelLibraryFilter();
        }
    }

    private void ChannelLibraryGroupFilterComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_isUpdatingGroupFilter && !_isUpdatingChannelLibraryControls)
        {
            ApplyChannelLibraryFilter();
        }
    }

    private void ChannelLibraryChannelListView_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        UpdateChannelLibraryCommandState();

    private void ChannelNumberingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingChannelLibraryControls ||
            _channelNumberingComboBox.SelectedItem is not ComboBoxItem
            {
                Tag: ChannelNumberingMode mode,
            } ||
            mode == _channelLibrarySnapshot.Settings.NumberingMode)
        {
            return;
        }

        _channelLibrarySnapshot = _channelLibrarySnapshot with
        {
            Settings = _channelLibrarySnapshot.Settings with { NumberingMode = mode },
        };
        SaveChannelLibrarySnapshot();
        RebuildChannelCatalog(rebuildGuide: true);
    }

    private void ShowHiddenChannelsCheckBox_Changed(object sender, RoutedEventArgs e) =>
        ApplyChannelLibraryFilter();

    private void ChannelFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelListView.SelectedItem is not ChannelListItem selected)
        {
            ShowChannelSelectionMessage();
            return;
        }

        var stableId = selected.Channel.StableId;
        var current = GetChannelOverride(stableId);
        var isFavorite = current?.IsFavorite != true;
        var updated = (current ?? CreateEmptyOverride(stableId)) with
        {
            IsFavorite = isFavorite,
            FavoriteOrder = isFavorite
                ? current?.FavoriteOrder ?? GetNextFavoriteOrder()
                : null,
        };
        SetChannelOverride(updated);
        SaveChannelLibrarySnapshot();
        RebuildChannelCatalog(rebuildGuide: true, preferredStableId: stableId);
    }

    private async void ChannelEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelListView.SelectedItem is not ChannelListItem selected)
        {
            ShowChannelSelectionMessage();
            return;
        }

        await ShowChannelEditorAsync(selected);
    }

    private async Task ShowChannelEditorAsync(ChannelListItem selected)
    {
        var stableId = selected.Channel.StableId;
        var current = GetChannelOverride(stableId);
        var customNameTextBox = new TextBox
        {
            Header = _channelLibraryResources.GetString("CustomNameLabel"),
            PlaceholderText = _channelLibraryResources.GetString("CustomNamePlaceholder"),
            Text = current?.CustomName ?? string.Empty,
        };
        var manualNumberBox = new NumberBox
        {
            Header = _channelLibraryResources.GetString("ManualNumberLabel"),
            Minimum = 1,
            Maximum = 99999,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Value = current?.ManualNumber is int existingManualNumber
                ? existingManualNumber
                : double.NaN,
        };
        var customCategoryTextBox = new TextBox
        {
            Header = _channelLibraryResources.GetString("CustomCategoryLabel"),
            PlaceholderText = _channelLibraryResources.GetString("CustomCategoryPlaceholder"),
            Text = current?.CustomCategory ?? string.Empty,
        };
        var favoriteCheckBox = new CheckBox
        {
            Content = _channelLibraryResources.GetString("FavoriteLabel"),
            IsChecked = current?.IsFavorite == true,
        };
        var hiddenCheckBox = new CheckBox
        {
            Content = _channelLibraryResources.GetString("HiddenEditorLabel"),
            IsChecked = current?.IsHidden == true,
        };

        var content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                customNameTextBox,
                manualNumberBox,
                customCategoryTextBox,
                favoriteCheckBox,
                hiddenCheckBox,
            },
        };
        var dialog = new ContentDialog
        {
            XamlRoot = ContentRoot.XamlRoot,
            Title = string.Format(
                CultureInfo.CurrentCulture,
                _channelLibraryResources.GetString("EditorTitleFormat"),
                selected.DisplayName),
            Content = content,
            PrimaryButtonText = _channelLibraryResources.GetString("Save"),
            SecondaryButtonText = _channelLibraryResources.GetString("Reset"),
            CloseButtonText = _channelLibraryResources.GetString("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            RemoveChannelOverride(stableId);
            SaveChannelLibrarySnapshot();
            RebuildChannelCatalog(rebuildGuide: true, preferredStableId: stableId);
            return;
        }

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var isFavorite = favoriteCheckBox.IsChecked == true;
        var editedManualNumber = double.IsNaN(manualNumberBox.Value)
            ? null
            : (int?)Math.Round(manualNumberBox.Value, MidpointRounding.AwayFromZero);
        var updated = new ChannelUserOverride(
            stableId,
            NormalizeEditorText(customNameTextBox.Text),
            editedManualNumber,
            isFavorite,
            isFavorite ? current?.FavoriteOrder ?? GetNextFavoriteOrder() : null,
            hiddenCheckBox.IsChecked == true,
            NormalizeEditorText(customCategoryTextBox.Text),
            current?.CustomOrder).Normalize();

        SetChannelOverride(updated);
        SaveChannelLibrarySnapshot();
        RebuildChannelCatalog(rebuildGuide: true, preferredStableId: stableId);
    }

    private void RebuildChannelCatalog(bool rebuildGuide, string? preferredStableId = null)
    {
        preferredStableId ??=
            (ChannelListView.SelectedItem as ChannelListItem)?.Channel.StableId ??
            _selectedPlaylistChannelStableId;

        _channelCatalog = _channelCatalogService.Build(_channels, _channelLibrarySnapshot);
        _favoriteChannelCatalog = _channelCatalogService.BuildFavorites(
            _channels,
            _channelLibrarySnapshot);
        PopulateChannelLibraryGroupFilter();
        ApplyChannelLibraryFilter(preferredStableId);
        UpdateSelectedChannelPresentation();

        if (rebuildGuide)
        {
            RebuildGuideChannelsFromCatalog();
        }
    }

    private void PopulateChannelLibraryGroupFilter()
    {
        var selectedTag = (GroupFilterComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        _isUpdatingGroupFilter = true;
        _isUpdatingChannelLibraryControls = true;
        GroupFilterComboBox.Items.Clear();
        GroupFilterComboBox.Items.Add(new ComboBoxItem
        {
            Content = _resources.GetString("PlaylistAllGroups"),
        });
        GroupFilterComboBox.Items.Add(new ComboBoxItem
        {
            Content = _channelLibraryResources.GetString("FavoritesFilter"),
            Tag = FavoritesGroupTag,
        });

        foreach (var category in _channelCatalog
                     .Select(static channel => channel.CategoryName)
                     .Where(static category => !string.IsNullOrWhiteSpace(category))
                     .Select(static category => category!)
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(static category => category, StringComparer.CurrentCultureIgnoreCase))
        {
            GroupFilterComboBox.Items.Add(new ComboBoxItem
            {
                Content = category,
                Tag = category,
            });
        }

        if (_channelCatalog.Any(static channel => string.IsNullOrWhiteSpace(channel.CategoryName)))
        {
            GroupFilterComboBox.Items.Add(new ComboBoxItem
            {
                Content = _resources.GetString("PlaylistNoGroup"),
                Tag = NoCategoryGroupTag,
            });
        }

        var selectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(selectedTag))
        {
            for (var index = 1; index < GroupFilterComboBox.Items.Count; index++)
            {
                if ((GroupFilterComboBox.Items[index] as ComboBoxItem)?.Tag is string tag &&
                    string.Equals(tag, selectedTag, StringComparison.CurrentCultureIgnoreCase))
                {
                    selectedIndex = index;
                    break;
                }
            }
        }

        GroupFilterComboBox.SelectedIndex = selectedIndex;
        _isUpdatingChannelLibraryControls = false;
        _isUpdatingGroupFilter = false;
        RefreshLiveCategoryRail();
    }

    private void ApplyChannelLibraryFilter(string? preferredStableId = null)
    {
        if (!_channelLibraryInitialized)
        {
            return;
        }

        preferredStableId ??=
            (ChannelListView.SelectedItem as ChannelListItem)?.Channel.StableId ??
            _selectedPlaylistChannelStableId;
        var search = ChannelSearchTextBox.Text?.Trim();
        var selectedGroup = (GroupFilterComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        IEnumerable<ChannelPresentation> query = selectedGroup == FavoritesGroupTag
            ? _favoriteChannelCatalog
            : _channelCatalog;

        if (_showHiddenChannelsCheckBox.IsChecked != true)
        {
            query = query.Where(static channel => !channel.IsHidden);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(channel =>
                channel.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                channel.ProviderChannel.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                (channel.ProviderChannel.TvgName?.Contains(
                    search,
                    StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (channel.ProviderChannel.TvgId?.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ?? false) ||
                (channel.Number?.ToString(CultureInfo.InvariantCulture).Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (selectedGroup == NoCategoryGroupTag)
        {
            query = query.Where(static channel => string.IsNullOrWhiteSpace(channel.CategoryName));
        }
        else if (!string.IsNullOrWhiteSpace(selectedGroup) && selectedGroup != FavoritesGroupTag)
        {
            query = query.Where(channel => string.Equals(
                channel.CategoryName,
                selectedGroup,
                StringComparison.CurrentCultureIgnoreCase));
        }

        var noGroup = _resources.GetString("PlaylistNoGroup");
        var hiddenLabel = _channelLibraryResources.GetString("HiddenLabel");
        var visibleItems = query
            .Select(channel => new ChannelListItem(
                channel,
                channel.CategoryName ?? noGroup,
                hiddenLabel))
            .ToList();
        EnrichLiveChannelRows(visibleItems);

        _isUpdatingChannelLibraryControls = true;
        ChannelListView.ItemsSource = visibleItems;
        var preferredIndex = visibleItems.FindIndex(item =>
            string.Equals(
                item.Channel.StableId,
                preferredStableId,
                StringComparison.Ordinal));
        ChannelListView.SelectedIndex = preferredIndex;
        _isUpdatingChannelLibraryControls = false;

        ChannelEmptyState.Visibility = visibleItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaylistSummaryText.Text = string.Format(
            CultureInfo.CurrentCulture,
            _channelLibraryResources.GetString("SummaryFormat"),
            visibleItems.Count,
            _channelCatalog.Count(static channel => !channel.IsHidden),
            _channelCatalog.Count(static channel => channel.IsFavorite),
            _channelCatalog.Count(static channel => channel.IsHidden));
        UpdateChannelLibraryCommandState();
        UpdateLivePresentationSelection();
    }

    private void RebuildGuideChannelsFromCatalog()
    {
        var preferredStableId =
            (GuideChannelComboBox.SelectedItem as EpgChannelListItem)?.Channel.StableId ??
            _selectedPlaylistChannelStableId;

        _guideChannels.Clear();
        _epgMatchResult = null;
        if (_epgDocument is not null)
        {
            _epgMatchResult = _epgChannelMatcher.Match(_channels, _epgDocument.Channels);
            foreach (var presentation in _channelCatalog.Where(static channel => !channel.IsHidden))
            {
                if (_epgMatchResult.PlaylistChannelMatches.TryGetValue(
                    presentation.ProviderChannel.StableId,
                    out var xmlTvChannelId))
                {
                    _guideChannels.Add(new EpgChannelListItem(presentation, xmlTvChannelId));
                }
            }
        }

        _isUpdatingGuideChannel = true;
        GuideChannelComboBox.ItemsSource = null;
        GuideChannelComboBox.ItemsSource = _guideChannels;
        GuideChannelComboBox.SelectedIndex = FindGuideChannelIndex(preferredStableId);
        _isUpdatingGuideChannel = false;

        UpdateEpgSummary();
        RefreshProgrammeList();
        if (_guideTimelineRefinementsInitialized)
        {
            CaptureAllTimelineGuideChannels();
        }
    }

    private void UpdateSelectedChannelPresentation()
    {
        if (string.IsNullOrWhiteSpace(_selectedPlaylistChannelStableId))
        {
            return;
        }

        var selected = _channelCatalog.FirstOrDefault(channel =>
            string.Equals(
                channel.ProviderChannel.StableId,
                _selectedPlaylistChannelStableId,
                StringComparison.Ordinal));
        if (selected is not null)
        {
            SelectedChannelText.Text = selected.NumberedName;
            LiveScreen.SetSelectedChannelHeader(selected.NumberedName, LiveScreen.NowTitle.Text);
        }
    }

    private void UpdateChannelLibraryCommandState()
    {
        if (_channelFavoriteButton is null || _channelEditButton is null)
        {
            return;
        }

        var selected = ChannelListView.SelectedItem as ChannelListItem;
        _channelFavoriteButton.IsEnabled = selected is not null;
        _channelEditButton.IsEnabled = selected is not null;
        _channelFavoriteButton.Content = selected?.IsFavorite == true ? "★" : "☆";

        if (_livePresentationResources is not null)
        {
            LiveScreen.SetFavoriteAction(
                selected?.IsFavorite == true,
                _livePresentationResources.GetString("LiveAddFavorite"),
                _livePresentationResources.GetString("LiveRemoveFavorite"));
        }
    }

    private ChannelUserOverride? GetChannelOverride(string stableId) =>
        _channelLibrarySnapshot.Overrides.TryGetValue(stableId, out var item) ? item : null;

    private void SetChannelOverride(ChannelUserOverride item)
    {
        item = item.Normalize();
        var overrides = new Dictionary<string, ChannelUserOverride>(
            _channelLibrarySnapshot.Overrides,
            StringComparer.Ordinal);
        if (item.IsEmpty)
        {
            overrides.Remove(item.StableId);
        }
        else
        {
            overrides[item.StableId] = item;
        }

        _channelLibrarySnapshot = new ChannelLibrarySnapshot(
            ChannelLibrarySnapshot.CurrentVersion,
            _channelLibrarySnapshot.Settings,
            overrides).Normalize();
    }

    private void RemoveChannelOverride(string stableId)
    {
        var overrides = new Dictionary<string, ChannelUserOverride>(
            _channelLibrarySnapshot.Overrides,
            StringComparer.Ordinal);
        overrides.Remove(stableId);
        _channelLibrarySnapshot = new ChannelLibrarySnapshot(
            ChannelLibrarySnapshot.CurrentVersion,
            _channelLibrarySnapshot.Settings,
            overrides);
    }

    private static ChannelUserOverride CreateEmptyOverride(string stableId) =>
        new(
            stableId,
            null,
            null,
            false,
            null,
            false,
            null,
            null);

    private int GetNextFavoriteOrder() =>
        _channelLibrarySnapshot.Overrides.Values
            .Where(static item => item.IsFavorite)
            .Select(static item => item.FavoriteOrder ?? 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

    private void SaveChannelLibrarySnapshot()
    {
        if (ChannelCustomizationStore.TrySave(_channelLibrarySnapshot))
        {
            return;
        }

        LiveScreen.ShowMessage(
            InfoBarSeverity.Warning,
            _channelLibraryResources.GetString("StoreErrorTitle"),
            _channelLibraryResources.GetString("StoreErrorMessage"));
    }

    private void ShowChannelSelectionMessage()
    {
        LiveScreen.ShowMessage(
            InfoBarSeverity.Informational,
            _channelLibraryResources.GetString("EditChannel"),
            _channelLibraryResources.GetString("SelectChannel"));
    }

    private static string? NormalizeEditorText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void ChannelLibraryWindow_Closed(object sender, WindowEventArgs args)
    {
        LoadPlaylistButton.Click -= ChannelLibraryLoadPlaylistButton_Click;
        LoadEpgButton.Click -= ChannelLibraryLoadEpgButton_Click;
        ChannelSearchTextBox.TextChanged -= ChannelLibrarySearchTextBox_TextChanged;
        GroupFilterComboBox.SelectionChanged -= ChannelLibraryGroupFilterComboBox_SelectionChanged;
        ChannelListView.SelectionChanged -= ChannelLibraryChannelListView_SelectionChanged;
        _channelNumberingComboBox.SelectionChanged -= ChannelNumberingComboBox_SelectionChanged;
        _channelFavoriteButton.Click -= ChannelFavoriteButton_Click;
        _channelEditButton.Click -= ChannelEditButton_Click;
        _showHiddenChannelsCheckBox.Checked -= ShowHiddenChannelsCheckBox_Changed;
        _showHiddenChannelsCheckBox.Unchecked -= ShowHiddenChannelsCheckBox_Changed;
        Closed -= ChannelLibraryWindow_Closed;
    }
}

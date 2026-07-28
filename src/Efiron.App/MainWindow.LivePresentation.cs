using Efiron.App.Playlists;
using Efiron.App.Presentation;
using Efiron.App.Startup;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Efiron.App;

public sealed partial class MainWindow
{
    private ResourceLoader _livePresentationResources = null!;
    private bool _deferredPresentationScheduled;
    private bool _deferredPresentationInitialized;
    private bool _isUpdatingLiveCategoryRail;

    internal void BeginDeferredPresentationInitialization()
    {
        if (_deferredPresentationScheduled || _deferredPresentationInitialized)
        {
            return;
        }

        _deferredPresentationScheduled = true;
        ContentRoot.Loaded += PresentationContentRoot_Loaded;
        DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            InitializeDeferredPresentation);
    }

    internal void ApplyStoredAppearanceBeforeActivation()
    {
        _appearanceSettings = Appearance.AppearanceSettingsStore.Load(out _);
        Appearance.AppearanceManager.Apply(RootNavigation, _appearanceSettings);
        ContentRoot.Background = Appearance.AppearanceManager.GetBrush("EfironAppBackgroundBrush");
    }

    private void PresentationContentRoot_Loaded(object sender, RoutedEventArgs e)
    {
        ContentRoot.Loaded -= PresentationContentRoot_Loaded;
        StartupTimeline.Mark("shell.loaded");
    }

    private void InitializeDeferredPresentation()
    {
        if (_deferredPresentationInitialized)
        {
            return;
        }

        _deferredPresentationScheduled = false;
        _deferredPresentationInitialized = true;
        StartupTimeline.Mark("presentation.deferred.start");

        _livePresentationResources = new ResourceLoader(
            ResourceLoader.GetDefaultResourceFilePath(),
            "LivePresentation");

        AttachLivePresentationEvents();
        InitializePlaylistWorkspace();
        InitializeEpgWorkspace();
        InitializeAppearanceWorkspace();
        InitializeLiveProgrammeWorkspace();
        ScheduleChannelLibraryWorkspaceInitialization();

        LiveScreen.SetHasChannels(_channels.Count > 0);
        StartupTimeline.Mark("live.view.ready");
        StartupTimeline.Mark("presentation.deferred.complete");
    }

    private void AttachLivePresentationEvents()
    {
        ChannelListView.ItemClick += ChannelListView_ItemClick;
        ChannelListView.SelectionChanged += LiveChannelListView_SelectionChanged;
        LiveScreen.Categories.SelectionChanged += LiveCategoryListView_SelectionChanged;
        LiveScreen.ConfigureSourcesButton.Click += LiveSourceSetupButton_Click;
        LiveScreen.WelcomeConfigureButton.Click += LiveSourceSetupButton_Click;
        LiveScreen.FocusChannelsAction.Click += LiveFocusChannelsButton_Click;
        LiveScreen.FavoriteAction.Click += LiveFavoriteActionButton_Click;
        LiveScreen.ProgrammeAction.Click += LiveProgrammeActionButton_Click;
        LiveScreen.ArchiveAction.Click += LiveUnavailableActionButton_Click;
        LiveScreen.MoreAction.Click += LiveUnavailableActionButton_Click;

        PlayerPlayPauseButton.Click += PlayerPlayPauseButton_Click;
        PlayerStopControlButton.Click += PlayerStopControlButton_Click;
        PlayerMuteButton.Click += PlayerMuteButton_Click;
        PlayerFullscreenButton.Click += PlayerFullscreenButton_Click;
        VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
        LivePlayerHost.PointerMoved += LivePlayerHost_PointerMoved;
        PlayerInputSurface.PointerMoved += LivePlayerHost_PointerMoved;
        PlayerInputSurface.DoubleTapped += LivePlayerHost_DoubleTapped;
        PlayerControlOverlay.PointerEntered += PlayerControlOverlay_PointerEntered;
        PlayerControlOverlay.PointerExited += PlayerControlOverlay_PointerExited;

        Closed += LivePresentationWindow_Closed;
    }

    private async void LiveSourceSetupButton_Click(object sender, RoutedEventArgs e)
    {
        var playlistBox = new TextBox
        {
            Header = _livePresentationResources.GetString("SourcePlaylistHeader"),
            PlaceholderText = _livePresentationResources.GetString("SourcePlaylistPlaceholder"),
            Text = PlaylistSourceTextBox.Text,
        };
        var epgBox = new TextBox
        {
            Header = _livePresentationResources.GetString("SourceEpgHeader"),
            PlaceholderText = _livePresentationResources.GetString("SourceEpgPlaceholder"),
            Text = EpgSourceTextBox.Text,
        };
        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = _livePresentationResources.GetString("SourceDialogDescription"),
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["EfironCaptionTextStyle"],
                },
                playlistBox,
                epgBox,
            },
        };
        var dialog = new ContentDialog
        {
            XamlRoot = ContentRoot.XamlRoot,
            Title = _livePresentationResources.GetString("SourceDialogTitle"),
            Content = content,
            PrimaryButtonText = _livePresentationResources.GetString("SourceDialogApply"),
            CloseButtonText = _livePresentationResources.GetString("SourceDialogCancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        PlaylistSourceTextBox.Text = playlistBox.Text.Trim();
        EpgSourceTextBox.Text = epgBox.Text.Trim();
        LiveScreen.ShowMessage(
            InfoBarSeverity.Informational,
            _livePresentationResources.GetString("LiveSourceSavedTitle"),
            _livePresentationResources.GetString("LiveSourceSavedMessage"));

        LoadPlaylistButton_Click(LoadPlaylistButton, new RoutedEventArgs());
        if (!string.IsNullOrWhiteSpace(EpgSourceTextBox.Text))
        {
            LoadEpgButton_Click(LoadEpgButton, new RoutedEventArgs());
        }
    }

    private void LiveCategoryListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingLiveCategoryRail ||
            LiveScreen.Categories.SelectedItem is not LiveCategoryItem category)
        {
            return;
        }

        var targetIndex = 0;
        for (var index = 0; index < GroupFilterComboBox.Items.Count; index++)
        {
            if (GroupFilterComboBox.Items[index] is not ComboBoxItem item)
            {
                continue;
            }

            var itemTag = item.Tag as string;
            if (string.Equals(
                    itemTag,
                    category.FilterTag,
                    StringComparison.CurrentCultureIgnoreCase) ||
                (itemTag is null && category.FilterTag is null))
            {
                targetIndex = index;
                break;
            }
        }

        GroupFilterComboBox.SelectedIndex = targetIndex;
    }

    internal void RefreshLiveCategoryRail()
    {
        if (_livePresentationResources is null || !_channelLibraryInitialized)
        {
            return;
        }

        var selectedTag =
            (LiveScreen.Categories.SelectedItem as LiveCategoryItem)?.FilterTag ??
            (GroupFilterComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        var categories = new List<LiveCategoryItem>
        {
            new(
                _livePresentationResources.GetString("LiveFavorites"),
                FavoritesGroupTag,
                "\uE734",
                _favoriteChannelCatalog.Count.ToString(System.Globalization.CultureInfo.CurrentCulture)),
            new(
                _livePresentationResources.GetString("LiveAllChannels"),
                null,
                "\uE8B7",
                _channelCatalog.Count(static item => !item.IsHidden)
                    .ToString(System.Globalization.CultureInfo.CurrentCulture)),
        };

        foreach (var item in GroupFilterComboBox.Items.OfType<ComboBoxItem>().Skip(2))
        {
            var tag = item.Tag as string;
            var name = item.Content?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            categories.Add(new LiveCategoryItem(name, tag, ResolveCategoryGlyph(name)));
        }

        _isUpdatingLiveCategoryRail = true;
        LiveScreen.Categories.ItemsSource = categories;
        var selectedIndex = categories.FindIndex(item => string.Equals(
            item.FilterTag,
            selectedTag,
            StringComparison.CurrentCultureIgnoreCase));
        LiveScreen.Categories.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 1;
        _isUpdatingLiveCategoryRail = false;
    }

    internal void EnrichLiveChannelRows(IReadOnlyList<ChannelListItem> items)
    {
        EnsureLiveScheduleIndex();
        var now = DateTimeOffset.Now;
        foreach (var item in items)
        {
            item.SetPlaying(string.Equals(
                item.Channel.StableId,
                _activeLiveChannelStableId,
                StringComparison.Ordinal));

            if (_liveScheduleIndex is null ||
                _epgMatchResult is null ||
                !_epgMatchResult.PlaylistChannelMatches.TryGetValue(
                    item.Channel.StableId,
                    out var xmlTvChannelId))
            {
                item.ApplyProgramme(
                    _livePresentationResources?.GetString("LiveProgrammeUnavailable"),
                    string.Empty);
                continue;
            }

            var nowNext = _liveScheduleIndex.Find(xmlTvChannelId, now);
            if (nowNext.Current is null)
            {
                item.ApplyProgramme(
                    _livePresentationResources?.GetString("LiveNoCurrent"),
                    string.Empty);
                continue;
            }

            item.ApplyProgramme(
                GetProgrammeTitle(nowNext.Current),
                FormatProgrammeRange(nowNext.Current.Start, nowNext.EffectiveCurrentStop));
        }
    }

    internal void UpdateLivePresentationSelection()
    {
        var selected = ChannelListView.SelectedItem as ChannelListItem;
        LiveScreen.SetFavoriteAction(
            selected?.IsFavorite == true,
            _livePresentationResources.GetString("LiveAddFavorite"),
            _livePresentationResources.GetString("LiveRemoveFavorite"));
        LiveScreen.SetHasChannels(ChannelListView.Items.Count > 0 || _channels.Count > 0);
    }

    private void LiveChannelListView_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateLivePresentationSelection();

    private void LiveFocusChannelsButton_Click(object sender, RoutedEventArgs e) =>
        ChannelListView.Focus(FocusState.Programmatic);

    private void LiveFavoriteActionButton_Click(object sender, RoutedEventArgs e) =>
        ChannelFavoriteButton_Click(sender, e);

    private void LiveProgrammeActionButton_Click(object sender, RoutedEventArgs e)
    {
        var title = LiveScreen.NowTitle.Text;
        var time = LiveScreen.NowTime.Text;
        LiveScreen.ShowMessage(
            InfoBarSeverity.Informational,
            string.IsNullOrWhiteSpace(title)
                ? _livePresentationResources.GetString("LiveNoCurrent")
                : title,
            time);
    }

    private void LiveUnavailableActionButton_Click(object sender, RoutedEventArgs e) =>
        LiveScreen.ShowMessage(
            InfoBarSeverity.Informational,
            _livePresentationResources.GetString("LiveNotImplementedTitle"),
            _livePresentationResources.GetString("LiveNotImplementedMessage"));

    private static string ResolveCategoryGlyph(string category)
    {
        if (category.Contains("спорт", StringComparison.CurrentCultureIgnoreCase) ||
            category.Contains("sport", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE7C1";
        }

        if (category.Contains("дет", StringComparison.CurrentCultureIgnoreCase) ||
            category.Contains("kid", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE77B";
        }

        if (category.Contains("музык", StringComparison.CurrentCultureIgnoreCase) ||
            category.Contains("music", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE8D6";
        }

        if (category.Contains("новост", StringComparison.CurrentCultureIgnoreCase) ||
            category.Contains("news", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE789";
        }

        if (category.Contains("фильм", StringComparison.CurrentCultureIgnoreCase) ||
            category.Contains("movie", StringComparison.CurrentCultureIgnoreCase))
        {
            return "\uE714";
        }

        return "\uE8B7";
    }

    private void LivePresentationWindow_Closed(object sender, WindowEventArgs args)
    {
        ChannelListView.ItemClick -= ChannelListView_ItemClick;
        ChannelListView.SelectionChanged -= LiveChannelListView_SelectionChanged;
        LiveScreen.Categories.SelectionChanged -= LiveCategoryListView_SelectionChanged;
        LiveScreen.ConfigureSourcesButton.Click -= LiveSourceSetupButton_Click;
        LiveScreen.WelcomeConfigureButton.Click -= LiveSourceSetupButton_Click;
        LiveScreen.FocusChannelsAction.Click -= LiveFocusChannelsButton_Click;
        LiveScreen.FavoriteAction.Click -= LiveFavoriteActionButton_Click;
        LiveScreen.ProgrammeAction.Click -= LiveProgrammeActionButton_Click;
        LiveScreen.ArchiveAction.Click -= LiveUnavailableActionButton_Click;
        LiveScreen.MoreAction.Click -= LiveUnavailableActionButton_Click;

        PlayerPlayPauseButton.Click -= PlayerPlayPauseButton_Click;
        PlayerStopControlButton.Click -= PlayerStopControlButton_Click;
        PlayerMuteButton.Click -= PlayerMuteButton_Click;
        PlayerFullscreenButton.Click -= PlayerFullscreenButton_Click;
        VolumeSlider.ValueChanged -= VolumeSlider_ValueChanged;
        LivePlayerHost.PointerMoved -= LivePlayerHost_PointerMoved;
        PlayerInputSurface.PointerMoved -= LivePlayerHost_PointerMoved;
        PlayerInputSurface.DoubleTapped -= LivePlayerHost_DoubleTapped;
        PlayerControlOverlay.PointerEntered -= PlayerControlOverlay_PointerEntered;
        PlayerControlOverlay.PointerExited -= PlayerControlOverlay_PointerExited;
        Closed -= LivePresentationWindow_Closed;
    }
}

using System.Collections.ObjectModel;
using System.Globalization;
using Efiron.Application.Live;
using Efiron.Application.Playback;
using Efiron.Desktop.Presentation;
using Efiron.Domain.Playback;
using LibVLCSharp.Platforms.Windows;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Efiron.Desktop.Views;

public sealed partial class LiveTvView : UserControl
{
    private readonly ObservableCollection<LiveChannelItem> _visibleItems = [];
    private readonly List<LiveChannelItem> _allItems = [];
    private readonly ResourceLoader _resources;

    private IPlaybackBackend? _playbackBackend;
    private IPlaybackSession? _playbackSession;
    private InitializedEventArgs? _libVlcInitialization;
    private LiveChannelItem? _selectedItem;
    private PlaybackRequest? _pendingPlaybackRequest;
    private PlaybackRequest? _currentPlaybackRequest;
    private bool _isUpdatingCategory;
    private bool _isUpdatingVolume;
    private bool _isFullscreen;

    public LiveTvView()
    {
        InitializeComponent();
        _resources = new ResourceLoader(
            ResourceLoader.GetDefaultResourceFilePath(),
            "Resources");
        ChannelListView.ItemsSource = _visibleItems;
        PlaybackStatusText.Text = _resources.GetString("PlaybackStatusReadyMessage");
        ChannelEmptyState.Visibility = Visibility.Visible;
        InitializePlaybackBackendController();
    }

    public event EventHandler? BackRequested;

    public event EventHandler? FullscreenToggleRequested;

    public event EventHandler<FavoriteChangedEventArgs>? FavoriteChanged;

    public event EventHandler<PlaybackSnapshotChangedEventArgs>? PlaybackSnapshotChanged;

    public void SetCatalog(
        LiveCatalogSnapshot catalog,
        IReadOnlySet<string> favoriteStableIds)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(favoriteStableIds);

        var preferredStableId = _selectedItem?.Snapshot.Channel.StableId;
        _allItems.Clear();
        var now = DateTimeOffset.Now;
        var noProgramme = _resources.GetString("LiveNoProgrammeMessage");
        var nextFormat = _resources.GetString("LiveNextProgrammeFormat");

        for (var index = 0; index < catalog.Channels.Count; index++)
        {
            var snapshot = catalog.Channels[index];
            _allItems.Add(new LiveChannelItem(
                index + 1,
                snapshot,
                favoriteStableIds.Contains(snapshot.Channel.StableId),
                now,
                noProgramme,
                nextFormat));
        }

        _selectedItem = !string.IsNullOrWhiteSpace(preferredStableId)
            ? _allItems.FirstOrDefault(item => string.Equals(
                item.Snapshot.Channel.StableId,
                preferredStableId,
                StringComparison.Ordinal))
            : null;
        _selectedItem ??= _allItems.FirstOrDefault();

        PopulateCategories();
        ApplyFilters();
        LiveSummaryText.Text = string.Format(
            CultureInfo.CurrentCulture,
            _resources.GetString("LiveSummaryFormat"),
            catalog.Channels.Count,
            catalog.Categories.Count,
            catalog.MatchedChannelCount);
        UpdateSelectedProgramme(_selectedItem);

        if (_selectedItem is not null)
        {
            SelectedChannelText.Text = _selectedItem.Name;
            SelectedProgrammeText.Text = _selectedItem.CurrentProgramme;
            ChannelListView.SelectedItem = _selectedItem;
        }
    }

    public async Task ActivateAsync()
    {
        _selectedItem ??= _allItems.FirstOrDefault();
        if (_selectedItem is null)
        {
            return;
        }

        if (_playbackSession is null)
        {
            await EnsurePlaybackBackendAsync();
        }

        var snapshot = _playbackSession?.Snapshot;
        if (snapshot?.Source == _selectedItem.Snapshot.Channel.StreamUri &&
            snapshot.State is PlaybackState.Opening or PlaybackState.Playing or PlaybackState.Paused)
        {
            return;
        }

        await SelectChannelAsync(_selectedItem);
    }

    public void SetFullscreen(bool isFullscreen)
    {
        _isFullscreen = isFullscreen;
        LiveHeader.Visibility = isFullscreen
            ? Visibility.Collapsed
            : Visibility.Visible;
        FullscreenIcon.Glyph = isFullscreen ? "\uE73F" : "\uE740";
        LiveRoot.Padding = isFullscreen
            ? new Thickness(0)
            : new Thickness(30, 24, 30, 30);
        PlayerSurfaceBorder.CornerRadius = isFullscreen
            ? new CornerRadius(0)
            : new CornerRadius(16);
        SetPlaybackBackendPanelFullscreen(isFullscreen);
    }

    public void DisposePlayback() =>
        DisposePlaybackBackendController();

    private void PopulateCategories()
    {
        var selectedCategory =
            (CategoryComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        _isUpdatingCategory = true;
        CategoryComboBox.Items.Clear();
        CategoryComboBox.Items.Add(new ComboBoxItem
        {
            Content = _resources.GetString("LiveAllCategoriesMessage"),
        });

        foreach (var category in _allItems
                     .Select(static item => item.Category)
                     .Where(static category => !string.IsNullOrWhiteSpace(category))
                     .Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            CategoryComboBox.Items.Add(new ComboBoxItem
            {
                Content = category,
                Tag = category,
            });
        }

        CategoryComboBox.SelectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(selectedCategory))
        {
            for (var index = 1; index < CategoryComboBox.Items.Count; index++)
            {
                if ((CategoryComboBox.Items[index] as ComboBoxItem)?.Tag is string category &&
                    string.Equals(
                        category,
                        selectedCategory,
                        StringComparison.CurrentCultureIgnoreCase))
                {
                    CategoryComboBox.SelectedIndex = index;
                    break;
                }
            }
        }

        _isUpdatingCategory = false;
    }

    private void ApplyFilters()
    {
        var search = ChannelSearchTextBox.Text.Trim();
        var category =
            (CategoryComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        IEnumerable<LiveChannelItem> query = _allItems;

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(item =>
                item.Name.Contains(
                    search,
                    StringComparison.CurrentCultureIgnoreCase) ||
                item.CurrentProgramme.Contains(
                    search,
                    StringComparison.CurrentCultureIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(item => string.Equals(
                item.Category,
                category,
                StringComparison.CurrentCultureIgnoreCase));
        }

        if (FavoritesOnlyButton.IsChecked is true)
        {
            query = query.Where(static item => item.IsFavorite);
        }

        var filtered = query.ToArray();
        _visibleItems.Clear();
        foreach (var item in filtered)
        {
            _visibleItems.Add(item);
        }

        ChannelEmptyState.Visibility = filtered.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChannelListView.Visibility = filtered.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async Task SelectChannelAsync(LiveChannelItem item)
    {
        _selectedItem = item;
        foreach (var candidate in _allItems)
        {
            candidate.IsPlaying = ReferenceEquals(candidate, item);
        }

        ChannelListView.SelectedItem = item;
        SelectedChannelText.Text = item.Name;
        SelectedProgrammeText.Text = item.CurrentProgramme;
        UpdateSelectedProgramme(item);

        var request = new PlaybackRequest(
            item.Snapshot.Channel.StreamUri,
            item.Snapshot.Channel.StableId,
            item.Name,
            item.Snapshot.Channel.PlaybackDirectives);
        _currentPlaybackRequest = request;
        _pendingPlaybackRequest = request;

        if (_playbackSession is null)
        {
            UpdatePlaybackStatus(
                PlaybackState.Opening,
                _resources.GetString("PlaybackStatusPreparingMessage"));
            return;
        }

        try
        {
            await _playbackSession.PlayAsync(request);
            _pendingPlaybackRequest = null;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            UpdatePlaybackStatus(
                PlaybackState.Failed,
                _resources.GetString("PlaybackStatusFailedMessage"));
        }
    }

    private void UpdateSelectedProgramme(LiveChannelItem? item)
    {
        if (item is null)
        {
            NowProgrammeTitle.Text = _resources.GetString("LiveProgrammeUnavailableMessage");
            NowProgrammeTime.Text = string.Empty;
            NextProgrammeTitle.Text = _resources.GetString("LiveProgrammeUnavailableMessage");
            NextProgrammeTime.Text = string.Empty;
            return;
        }

        var current = item.Snapshot.CurrentProgramme;
        NowProgrammeTitle.Text = current?.Title ??
            _resources.GetString("LiveProgrammeUnavailableMessage");
        NowProgrammeTime.Text = FormatProgrammeTime(current?.Start, current?.Stop);

        var next = item.Snapshot.NextProgramme;
        NextProgrammeTitle.Text = next?.Title ??
            _resources.GetString("LiveProgrammeUnavailableMessage");
        NextProgrammeTime.Text = FormatProgrammeTime(next?.Start, next?.Stop);
    }

    private static string FormatProgrammeTime(
        DateTimeOffset? start,
        DateTimeOffset? stop)
    {
        if (start is null)
        {
            return string.Empty;
        }

        var localStart = start.Value.ToLocalTime();
        return stop is null
            ? localStart.ToString("HH:mm", CultureInfo.CurrentCulture)
            : $"{localStart:HH:mm}–{stop.Value.ToLocalTime():HH:mm}";
    }

    private async void VideoView_Initialized(
        object? sender,
        InitializedEventArgs e)
    {
        _libVlcInitialization = e;
        if (_playbackBackend is not null || Visibility != Visibility.Visible)
        {
            return;
        }

        try
        {
            await EnsurePlaybackBackendAsync();
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            UpdatePlaybackStatus(
                PlaybackState.Failed,
                _resources.GetString("PlaybackStatusFailedMessage"));
        }
    }

    private void PlaybackSession_SnapshotChanged(
        object? sender,
        PlaybackSnapshotChangedEventArgs e)
    {
        PlaybackSnapshotChanged?.Invoke(this, e);
        _playbackDiagnosticsWriter.RequestRecord();
        DispatcherQueue.TryEnqueue(() => ApplyPlaybackSnapshot(e.Snapshot));
    }

    private void ApplyPlaybackSnapshot(PlaybackSnapshot snapshot)
    {
        var statusText = snapshot.State switch
        {
            PlaybackState.Opening => _resources.GetString("PlaybackStatusOpeningMessage"),
            PlaybackState.Playing => _resources.GetString("PlaybackStatusPlayingMessage"),
            PlaybackState.Paused => _resources.GetString("PlaybackStatusPausedMessage"),
            PlaybackState.Stopped => _resources.GetString("PlaybackStatusStoppedMessage"),
            PlaybackState.Ended => _resources.GetString("PlaybackStatusEndedMessage"),
            PlaybackState.Failed => _resources.GetString("PlaybackStatusFailedMessage"),
            PlaybackState.Disposed => _resources.GetString("PlaybackStatusDisposedMessage"),
            _ => _resources.GetString("PlaybackStatusReadyMessage"),
        };
        UpdatePlaybackStatus(snapshot.State, statusText);

        PlayerOpeningIndicator.IsActive = snapshot.State == PlaybackState.Opening;
        PlayerOpeningIndicator.Visibility = snapshot.State == PlaybackState.Opening
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlayerEmptyState.Visibility = snapshot.State is PlaybackState.Playing or PlaybackState.Paused
            ? Visibility.Collapsed
            : Visibility.Visible;
        PlayPauseIcon.Glyph = snapshot.State == PlaybackState.Playing
            ? "\uE769"
            : "\uE768";
        MuteIcon.Glyph = snapshot.IsMuted || snapshot.Volume == 0
            ? "\uE74F"
            : "\uE767";

        _isUpdatingVolume = true;
        VolumeSlider.Value = snapshot.Volume;
        _isUpdatingVolume = false;
    }

    private void UpdatePlaybackStatus(
        PlaybackState state,
        string text)
    {
        PlaybackStatusText.Text = text;
        PlaybackStatusDot.Fill = new SolidColorBrush(state switch
        {
            PlaybackState.Playing => ColorHelper.FromArgb(255, 54, 199, 139),
            PlaybackState.Opening => ColorHelper.FromArgb(255, 37, 134, 255),
            PlaybackState.Paused => ColorHelper.FromArgb(255, 231, 168, 63),
            PlaybackState.Failed => ColorHelper.FromArgb(255, 226, 80, 80),
            _ => ColorHelper.FromArgb(255, 116, 129, 150),
        });
    }

    private void BackSourcesButton_Click(object sender, RoutedEventArgs e) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void ChannelSearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e) =>
        ApplyFilters();

    private void CategoryComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_isUpdatingCategory)
        {
            ApplyFilters();
        }
    }

    private void FavoritesOnlyButton_Checked(
        object sender,
        RoutedEventArgs e) =>
        ApplyFilters();

    private async void ChannelListView_ItemClick(
        object sender,
        ItemClickEventArgs e)
    {
        if (e.ClickedItem is LiveChannelItem item)
        {
            await SelectChannelAsync(item);
        }
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LiveChannelItem item })
        {
            return;
        }

        item.IsFavorite = !item.IsFavorite;
        FavoriteChanged?.Invoke(
            this,
            new FavoriteChangedEventArgs(
                item.Snapshot.Channel.StableId,
                item.IsFavorite));

        if (FavoritesOnlyButton.IsChecked is true)
        {
            ApplyFilters();
        }
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_playbackSession?.Snapshot.State == PlaybackState.Playing)
        {
            _playbackSession.Pause();
            return;
        }

        if (_playbackSession?.Snapshot.State == PlaybackState.Paused)
        {
            _playbackSession.Resume();
            return;
        }

        if (_selectedItem is not null)
        {
            await SelectChannelAsync(_selectedItem);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) =>
        _playbackSession?.Stop();

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_playbackSession is null)
        {
            return;
        }

        _playbackSession.SetMuted(!_playbackSession.Snapshot.IsMuted);
    }

    private void VolumeSlider_ValueChanged(
        object sender,
        RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingVolume || _playbackSession is null)
        {
            return;
        }

        _playbackSession.SetVolume((int)Math.Round(e.NewValue));
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e) =>
        FullscreenToggleRequested?.Invoke(this, EventArgs.Empty);

    private void PlayerInputSurface_DoubleTapped(
        object sender,
        DoubleTappedRoutedEventArgs e)
    {
        FullscreenToggleRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
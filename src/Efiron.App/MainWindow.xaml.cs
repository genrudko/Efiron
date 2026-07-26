using System.Globalization;
using Efiron.App.Localization;
using Efiron.App.Playlists;
using Efiron.Core.Playback;
using Efiron.Core.Playlists;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;

namespace Efiron.App;

public sealed partial class MainWindow : Window
{
    private static readonly HttpClient PlaylistHttpClient = CreatePlaylistHttpClient();

    private readonly M3uPlaylistParser _playlistParser = new();
    private readonly RemotePlaylistClient _playlistClient = new(PlaylistHttpClient);
    private readonly List<PlaylistChannel> _channels = [];

    private ResourceLoader _resources = null!;
    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private CancellationTokenSource? _playlistLoadCancellation;
    private bool _isClosing;
    private bool _isSelectingInitialLanguage;
    private bool _isUpdatingGroupFilter;

    public MainWindow()
    {
        InitializeComponent();

        _resources = new ResourceLoader();
        Title = _resources.GetString("WindowTitle");
        StatusText.Text = _resources.GetString("StatusReady");

        VideoView.Initialized += VideoView_Initialized;
        Closed += MainWindow_Closed;

        RootNavigation.SelectedItem = LiveNavigationItem;
        SelectConfiguredLanguage();
        InitializePlaylistWorkspace();
        ShowSection("live");
    }

    private static HttpClient CreatePlaylistHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Efiron/0.1");
        return client;
    }

    private void InitializePlaylistWorkspace()
    {
        var savedSource = PlaylistSourceStore.Load();
        if (savedSource is not null)
        {
            PlaylistSourceTextBox.Text = savedSource.AbsoluteUri;
        }

        PopulateGroupFilter();
        ApplyChannelFilter();
    }

    private void VideoView_Initialized(object? sender, InitializedEventArgs e)
    {
        _libVlc = new LibVLC(enableDebugLogs: true, e.SwapChainOptions);
        _mediaPlayer = new MediaPlayer(_libVlc);
        _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
        VideoView.MediaPlayer = _mediaPlayer;
        StatusText.Text = _resources.GetString("StatusMediaReady");
    }

    private async void LoadPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPlaylistSource(out var source))
        {
            ShowPlaylistError(_resources.GetString("PlaylistInvalidAddress"));
            StatusText.Text = _resources.GetString("StatusPlaylistLoadFailed");
            return;
        }

        _playlistLoadCancellation?.Cancel();
        _playlistLoadCancellation?.Dispose();
        var loadCancellation = new CancellationTokenSource();
        _playlistLoadCancellation = loadCancellation;

        PlaylistInfoBar.IsOpen = false;
        SetPlaylistLoading(true);
        StatusText.Text = _resources.GetString("StatusDownloadingPlaylist");

        try
        {
            var content = await _playlistClient.DownloadAsync(source!, loadCancellation.Token);
            var result = _playlistParser.Parse(content, source);
            if (result.Channels.Count == 0)
            {
                ShowPlaylistError(_resources.GetString("PlaylistNoChannels"));
                StatusText.Text = _resources.GetString("StatusPlaylistLoadFailed");
                return;
            }

            _channels.Clear();
            _channels.AddRange(result.Channels);
            PlaylistSourceStore.TrySave(source!);
            PopulateGroupFilter();
            ApplyChannelFilter();

            if (result.Warnings.Count > 0)
            {
                PlaylistInfoBar.Severity = InfoBarSeverity.Warning;
                PlaylistInfoBar.Title = _resources.GetString("PlaylistWarningTitle");
                PlaylistInfoBar.Message = string.Format(
                    CultureInfo.CurrentCulture,
                    _resources.GetString("PlaylistWarningMessageFormat"),
                    result.Warnings.Count);
                PlaylistInfoBar.IsOpen = true;
            }

            StatusText.Text = string.Format(
                CultureInfo.CurrentCulture,
                _resources.GetString("StatusPlaylistLoadedFormat"),
                result.Channels.Count);
        }
        catch (OperationCanceledException) when (loadCancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            ShowPlaylistError(_resources.GetString("PlaylistRequestTimedOut"));
            StatusText.Text = _resources.GetString("StatusPlaylistLoadFailed");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidDataException or IOException or NotSupportedException)
        {
            ShowPlaylistError(_resources.GetString("PlaylistLoadError"));
            StatusText.Text = _resources.GetString("StatusPlaylistLoadFailed");
        }
        finally
        {
            if (ReferenceEquals(_playlistLoadCancellation, loadCancellation))
            {
                _playlistLoadCancellation = null;
                SetPlaylistLoading(false);
            }

            loadCancellation.Dispose();
        }
    }

    private bool TryGetPlaylistSource(out Uri? source)
    {
        if (!Uri.TryCreate(PlaylistSourceTextBox.Text?.Trim(), UriKind.Absolute, out source))
        {
            return false;
        }

        return source.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            source.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private void SetPlaylistLoading(bool isLoading)
    {
        LoadPlaylistButton.IsEnabled = !isLoading;
        PlaylistSourceTextBox.IsEnabled = !isLoading;
        PlaylistProgressRing.IsActive = isLoading;
        PlaylistProgressRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowPlaylistError(string message)
    {
        PlaylistInfoBar.Severity = InfoBarSeverity.Error;
        PlaylistInfoBar.Title = _resources.GetString("PlaylistErrorTitle");
        PlaylistInfoBar.Message = message;
        PlaylistInfoBar.IsOpen = true;
    }

    private void PopulateGroupFilter()
    {
        _isUpdatingGroupFilter = true;
        GroupFilterComboBox.Items.Clear();
        GroupFilterComboBox.Items.Add(new ComboBoxItem
        {
            Content = _resources.GetString("PlaylistAllGroups"),
        });

        var groups = _channels
            .Select(channel => channel.GroupName)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Select(group => group!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => group, StringComparer.CurrentCultureIgnoreCase);

        foreach (var group in groups)
        {
            GroupFilterComboBox.Items.Add(new ComboBoxItem
            {
                Content = group,
                Tag = group,
            });
        }

        GroupFilterComboBox.SelectedIndex = 0;
        _isUpdatingGroupFilter = false;
    }

    private void ChannelSearchTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyChannelFilter();

    private void GroupFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUpdatingGroupFilter)
        {
            ApplyChannelFilter();
        }
    }

    private void ApplyChannelFilter()
    {
        var search = ChannelSearchTextBox.Text?.Trim();
        var selectedGroup = (GroupFilterComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        IEnumerable<PlaylistChannel> query = _channels;

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(channel =>
                channel.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                (channel.TvgName?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                (channel.TvgId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.IsNullOrWhiteSpace(selectedGroup))
        {
            query = query.Where(channel =>
                string.Equals(channel.GroupName, selectedGroup, StringComparison.CurrentCultureIgnoreCase));
        }

        var noGroup = _resources.GetString("PlaylistNoGroup");
        var visibleItems = query
            .Select(channel => new ChannelListItem(channel, channel.GroupName ?? noGroup))
            .ToList();

        ChannelListView.ItemsSource = visibleItems;
        ChannelEmptyState.Visibility = visibleItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PlaylistSummaryText.Text = string.Format(
            CultureInfo.CurrentCulture,
            _resources.GetString("PlaylistSummaryFormat"),
            visibleItems.Count,
            _channels.Count);
    }

    private void ChannelListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ChannelListItem item)
        {
            return;
        }

        SourceTextBox.Text = item.Channel.StreamUri.AbsoluteUri;
        SelectedChannelText.Text = item.Name;
        StartPlayback(item.Channel.StreamUri, item.Name);
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(SourceTextBox.Text?.Trim(), UriKind.Absolute, out var source))
        {
            StatusText.Text = _resources.GetString("StatusInvalidSource");
            return;
        }

        StartPlayback(source, null);
    }

    private void StartPlayback(Uri source, string? channelName)
    {
        if (_libVlc is null || _mediaPlayer is null)
        {
            StatusText.Text = _resources.GetString("StatusMediaNotReady");
            return;
        }

        try
        {
            var request = new PlaybackRequest(source);
            _currentMedia?.Dispose();
            _currentMedia = new Media(_libVlc, request.Source);
            _mediaPlayer.Play(_currentMedia);
            PlayerEmptyState.Visibility = Visibility.Collapsed;
            StatusText.Text = channelName is null
                ? _resources.GetString("StatusOpeningStream")
                : string.Format(
                    CultureInfo.CurrentCulture,
                    _resources.GetString("StatusOpeningChannelFormat"),
                    channelName);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        _mediaPlayer.Pause();
        StatusText.Text = _resources.GetString("StatusPlaybackToggled");
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaPlayer is null)
        {
            return;
        }

        _mediaPlayer.Stop();
        PlayerEmptyState.Visibility = Visibility.Visible;
        StatusText.Text = _resources.GetString("StatusStopped");
    }

    private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = _resources.GetString("StatusPlaybackError");
            PlayerEmptyState.Visibility = Visibility.Visible;
        });
    }

    private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ShowSection("settings");
            return;
        }

        if (args.SelectedItemContainer?.Tag is string tag)
        {
            ShowSection(tag);
        }
    }

    private void ShowSection(string section)
    {
        LiveView.Visibility = section == "live" ? Visibility.Visible : Visibility.Collapsed;
        GuideView.Visibility = section == "guide" ? Visibility.Visible : Visibility.Collapsed;
        ArchiveView.Visibility = section == "archive" ? Visibility.Visible : Visibility.Collapsed;
        RecordingsView.Visibility = section == "recordings" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = section == "settings" ? Visibility.Visible : Visibility.Collapsed;

        HeaderTitle.Text = _resources.GetString(section switch
        {
            "guide" => "HeaderGuide",
            "archive" => "HeaderArchive",
            "recordings" => "HeaderRecordings",
            "settings" => "HeaderSettings",
            _ => "HeaderLive",
        });
    }

    private void SelectConfiguredLanguage()
    {
        var language = AppLanguageStore.Load();
        if (string.IsNullOrWhiteSpace(language))
        {
            language = ApplicationLanguages.Languages.FirstOrDefault() ?? "en-US";
        }

        _isSelectingInitialLanguage = true;
        LanguageComboBox.SelectedIndex = language.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        _isSelectingInitialLanguage = false;
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSelectingInitialLanguage ||
            LanguageComboBox.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string language)
        {
            return;
        }

        AppLanguageStore.Save(language);
        ApplicationLanguages.PrimaryLanguageOverride = language;
        RestartInfoBar.IsOpen = true;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _playlistLoadCancellation?.Cancel();
        _playlistLoadCancellation?.Dispose();

        if (_mediaPlayer is not null)
        {
            _mediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
            _mediaPlayer.Stop();
        }

        LivePlayerHost.Children.Remove(VideoView);
        _currentMedia?.Dispose();
        _mediaPlayer?.Dispose();
        _libVlc?.Dispose();
    }
}

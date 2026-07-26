using System.Globalization;
using System.Xml;
using Efiron.App.Epg;
using Efiron.App.Localization;
using Efiron.App.Playlists;
using Efiron.Core.Epg;
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
    private static readonly HttpClient PlaylistHttpClient = CreateHttpClient(TimeSpan.FromSeconds(30));
    private static readonly HttpClient EpgHttpClient = CreateHttpClient(TimeSpan.FromSeconds(60));

    private readonly M3uPlaylistParser _playlistParser = new();
    private readonly RemotePlaylistClient _playlistClient = new(PlaylistHttpClient);
    private readonly List<PlaylistChannel> _channels = [];
    private readonly XmlTvParser _xmlTvParser = new();
    private readonly EpgChannelMatcher _epgChannelMatcher = new();
    private readonly RemoteEpgClient _epgClient = new(EpgHttpClient);
    private readonly List<EpgChannelListItem> _guideChannels = [];

    private ResourceLoader _resources = null!;
    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private XmlTvDocument? _epgDocument;
    private EpgMatchResult? _epgMatchResult;
    private CancellationTokenSource? _playlistLoadCancellation;
    private CancellationTokenSource? _epgLoadCancellation;
    private string? _selectedPlaylistChannelStableId;
    private bool _isClosing;
    private bool _isSelectingInitialLanguage;
    private bool _isUpdatingGroupFilter;
    private bool _isUpdatingGuideChannel;
    private bool _isUpdatingGuideDate;

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
        InitializeEpgWorkspace();
        ShowSection("live");
    }

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var client = new HttpClient
        {
            Timeout = timeout,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Efiron/0.2");
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

    private void InitializeEpgWorkspace()
    {
        var savedSource = EpgSourceStore.Load();
        if (savedSource is not null)
        {
            EpgSourceTextBox.Text = savedSource.AbsoluteUri;
        }

        SetGuideDate(DateOnly.FromDateTime(DateTime.Today));
        UpdateEpgSummary();
        RefreshProgrammeList();
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
            TryApplyDiscoveredEpgSource(result.HeaderAttributes, source!);
            PopulateGroupFilter();
            ApplyChannelFilter();
            RebuildGuideChannels();

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

        return IsHttpSource(source);
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

        _selectedPlaylistChannelStableId = item.Channel.StableId;
        SelectGuideChannelByStableId(item.Channel.StableId);
        SourceTextBox.Text = item.Channel.StreamUri.AbsoluteUri;
        SelectedChannelText.Text = item.Name;
        StartPlayback(item.Channel.StreamUri, item.Name);
    }

    private async void LoadEpgButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetEpgSource(out var source))
        {
            ShowEpgError(_resources.GetString("EpgInvalidAddress"));
            StatusText.Text = _resources.GetString("StatusEpgLoadFailed");
            return;
        }

        _epgLoadCancellation?.Cancel();
        _epgLoadCancellation?.Dispose();
        var loadCancellation = new CancellationTokenSource();
        _epgLoadCancellation = loadCancellation;

        EpgInfoBar.IsOpen = false;
        SetEpgLoading(true);
        StatusText.Text = _resources.GetString("StatusDownloadingEpg");

        try
        {
            await using var content = await _epgClient.DownloadAsync(source!, loadCancellation.Token);
            var document = _xmlTvParser.Parse(content);
            if (document.Channels.Count == 0 || document.Programmes.Count == 0)
            {
                ShowEpgError(_resources.GetString("EpgNoProgrammes"));
                StatusText.Text = _resources.GetString("StatusEpgLoadFailed");
                return;
            }

            _epgDocument = document;
            EpgSourceStore.TrySave(source!);
            RebuildGuideChannels();

            if (document.Warnings.Count > 0)
            {
                EpgInfoBar.Severity = InfoBarSeverity.Warning;
                EpgInfoBar.Title = _resources.GetString("EpgWarningTitle");
                EpgInfoBar.Message = string.Format(
                    CultureInfo.CurrentCulture,
                    _resources.GetString("EpgWarningMessageFormat"),
                    document.Warnings.Count);
                EpgInfoBar.IsOpen = true;
            }

            StatusText.Text = string.Format(
                CultureInfo.CurrentCulture,
                _resources.GetString("StatusEpgLoadedFormat"),
                document.Programmes.Count);
        }
        catch (OperationCanceledException) when (loadCancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            ShowEpgError(_resources.GetString("EpgRequestTimedOut"));
            StatusText.Text = _resources.GetString("StatusEpgLoadFailed");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidDataException or IOException or NotSupportedException or XmlException)
        {
            ShowEpgError(_resources.GetString("EpgLoadError"));
            StatusText.Text = _resources.GetString("StatusEpgLoadFailed");
        }
        finally
        {
            if (ReferenceEquals(_epgLoadCancellation, loadCancellation))
            {
                _epgLoadCancellation = null;
                SetEpgLoading(false);
            }

            loadCancellation.Dispose();
        }
    }

    private bool TryGetEpgSource(out Uri? source)
    {
        if (!Uri.TryCreate(EpgSourceTextBox.Text?.Trim(), UriKind.Absolute, out source))
        {
            return false;
        }

        return IsHttpSource(source);
    }

    private static bool IsHttpSource(Uri source) =>
        source.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
        source.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private void SetEpgLoading(bool isLoading)
    {
        LoadEpgButton.IsEnabled = !isLoading;
        EpgSourceTextBox.IsEnabled = !isLoading;
        EpgProgressRing.IsActive = isLoading;
        EpgProgressRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowEpgError(string message)
    {
        EpgInfoBar.Severity = InfoBarSeverity.Error;
        EpgInfoBar.Title = _resources.GetString("EpgErrorTitle");
        EpgInfoBar.Message = message;
        EpgInfoBar.IsOpen = true;
    }

    private void TryApplyDiscoveredEpgSource(
        IReadOnlyDictionary<string, string> headerAttributes,
        Uri playlistSource)
    {
        if (!string.IsNullOrWhiteSpace(EpgSourceTextBox.Text))
        {
            return;
        }

        foreach (var key in new[] { "url-tvg", "x-tvg-url", "tvg-url" })
        {
            if (!headerAttributes.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var candidate = value
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var discovered))
            {
                Uri.TryCreate(playlistSource, candidate, out discovered);
            }

            if (discovered is not null && IsHttpSource(discovered))
            {
                EpgSourceTextBox.Text = discovered.AbsoluteUri;
                return;
            }
        }
    }

    private void RebuildGuideChannels()
    {
        var preferredStableId =
            (GuideChannelComboBox.SelectedItem as EpgChannelListItem)?.Channel.StableId ??
            _selectedPlaylistChannelStableId;

        _guideChannels.Clear();
        _epgMatchResult = null;

        if (_epgDocument is not null)
        {
            _epgMatchResult = _epgChannelMatcher.Match(_channels, _epgDocument.Channels);
            foreach (var channel in _channels)
            {
                if (_epgMatchResult.PlaylistChannelMatches.TryGetValue(channel.StableId, out var xmlTvChannelId))
                {
                    _guideChannels.Add(new EpgChannelListItem(channel, xmlTvChannelId));
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
    }

    private int FindGuideChannelIndex(string? stableId)
    {
        if (!string.IsNullOrWhiteSpace(stableId))
        {
            var index = _guideChannels.FindIndex(item => item.Channel.StableId == stableId);
            if (index >= 0)
            {
                return index;
            }
        }

        return _guideChannels.Count > 0 ? 0 : -1;
    }

    private void SelectGuideChannelByStableId(string stableId)
    {
        var index = FindGuideChannelIndex(stableId);
        if (index >= 0 && GuideChannelComboBox.SelectedIndex != index)
        {
            GuideChannelComboBox.SelectedIndex = index;
        }
    }

    private void UpdateEpgSummary()
    {
        if (_epgDocument is null)
        {
            EpgSummaryText.Text = _resources.GetString("EpgNotLoadedSummary");
            return;
        }

        EpgSummaryText.Text = string.Format(
            CultureInfo.CurrentCulture,
            _resources.GetString("EpgSummaryFormat"),
            _epgDocument.Channels.Count,
            _epgDocument.Programmes.Count,
            _guideChannels.Count,
            _epgMatchResult?.ExactIdMatches ?? 0,
            _epgMatchResult?.UniqueNameMatches ?? 0);
    }

    private void GuideChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingGuideChannel)
        {
            return;
        }

        if (GuideChannelComboBox.SelectedItem is EpgChannelListItem item)
        {
            _selectedPlaylistChannelStableId = item.Channel.StableId;
        }

        RefreshProgrammeList();
    }

    private void GuideDatePicker_DateChanged(
        CalendarDatePicker sender,
        CalendarDatePickerDateChangedEventArgs args)
    {
        if (!_isUpdatingGuideDate)
        {
            RefreshProgrammeList();
        }
    }

    private void GuidePreviousDayButton_Click(object sender, RoutedEventArgs e) =>
        SetGuideDate(GetSelectedGuideDate().AddDays(-1));

    private void GuideTodayButton_Click(object sender, RoutedEventArgs e) =>
        SetGuideDate(DateOnly.FromDateTime(DateTime.Today));

    private void GuideNextDayButton_Click(object sender, RoutedEventArgs e) =>
        SetGuideDate(GetSelectedGuideDate().AddDays(1));

    private void SetGuideDate(DateOnly date)
    {
        var localDateTime = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var offset = TimeZoneInfo.Local.GetUtcOffset(localDateTime);

        _isUpdatingGuideDate = true;
        GuideDatePicker.Date = new DateTimeOffset(localDateTime, offset);
        _isUpdatingGuideDate = false;
        RefreshProgrammeList();
    }

    private DateOnly GetSelectedGuideDate()
    {
        var selected = GuideDatePicker.Date ?? DateTimeOffset.Now;
        return DateOnly.FromDateTime(selected.LocalDateTime);
    }

    private void RefreshProgrammeList()
    {
        ClearProgrammeDetails();

        if (_epgDocument is null ||
            GuideChannelComboBox.SelectedItem is not EpgChannelListItem selectedChannel)
        {
            ProgrammeListView.ItemsSource = Array.Empty<ProgrammeListItem>();
            ProgrammeEmptyState.Visibility = Visibility.Visible;
            return;
        }

        var selectedDate = GetSelectedGuideDate();
        var items = _epgDocument.Programmes
            .Where(programme =>
                programme.ChannelId.Equals(selectedChannel.XmlTvChannelId, StringComparison.OrdinalIgnoreCase) &&
                DateOnly.FromDateTime(programme.Start.ToLocalTime().DateTime) == selectedDate)
            .OrderBy(static programme => programme.Start)
            .Select(CreateProgrammeListItem)
            .ToList();

        ProgrammeListView.ItemsSource = items;
        ProgrammeEmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (items.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var currentIndex = items.FindIndex(item =>
            item.Programme.Start <= now &&
            (item.Programme.Stop is null || item.Programme.Stop > now));
        ProgrammeListView.SelectedIndex = currentIndex >= 0 ? currentIndex : 0;
    }

    private ProgrammeListItem CreateProgrammeListItem(XmlTvProgramme programme)
    {
        var start = programme.Start.ToLocalTime();
        var timeRange = programme.Stop is null
            ? string.Format(CultureInfo.CurrentCulture, "{0:t}–…", start)
            : string.Format(
                CultureInfo.CurrentCulture,
                "{0:t}–{1:t}",
                start,
                programme.Stop.Value.ToLocalTime());

        var title = string.IsNullOrWhiteSpace(programme.Title)
            ? _resources.GetString("ProgrammeUntitled")
            : programme.Title;
        var subtitle = programme.Subtitle ?? string.Empty;
        var categories = string.Join(" • ", programme.Categories);
        var description = programme.Description ?? _resources.GetString("ProgrammeNoDescription");

        return new ProgrammeListItem(
            programme,
            timeRange,
            title,
            subtitle,
            categories,
            description);
    }

    private void ProgrammeListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProgrammeListView.SelectedItem is not ProgrammeListItem item)
        {
            ClearProgrammeDetails();
            return;
        }

        var localStart = item.Programme.Start.ToLocalTime();
        ProgrammeDetailsTime.Text = string.Format(
            CultureInfo.CurrentCulture,
            "{0:d} • {1}",
            localStart,
            item.TimeRange);
        ProgrammeDetailsName.Text = item.Title;
        ProgrammeDetailsSubtitle.Text = item.Subtitle;
        ProgrammeDetailsSubtitle.Visibility = string.IsNullOrWhiteSpace(item.Subtitle)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ProgrammeDetailsCategories.Text = item.Categories;
        ProgrammeDetailsCategories.Visibility = string.IsNullOrWhiteSpace(item.Categories)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ProgrammeDetailsDescription.Text = item.Description;
    }

    private void ClearProgrammeDetails()
    {
        ProgrammeDetailsTime.Text = string.Empty;
        ProgrammeDetailsName.Text = _resources.GetString("ProgrammeDetailsEmpty");
        ProgrammeDetailsSubtitle.Text = string.Empty;
        ProgrammeDetailsSubtitle.Visibility = Visibility.Collapsed;
        ProgrammeDetailsCategories.Text = string.Empty;
        ProgrammeDetailsCategories.Visibility = Visibility.Collapsed;
        ProgrammeDetailsDescription.Text = string.Empty;
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

        if (section == "guide")
        {
            SelectGuideChannelByStableId(_selectedPlaylistChannelStableId ?? string.Empty);
            RefreshProgrammeList();
        }

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
        _epgLoadCancellation?.Cancel();
        _epgLoadCancellation?.Dispose();

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
